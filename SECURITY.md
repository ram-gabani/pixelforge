# Security Policy & Architecture

This document describes the security architecture, threat model, input validation controls, defenses, and vulnerability reporting procedures for **PixelForge**.

---

## 1. Security Architecture Overview

`PixelForge` is an ASP.NET Core MVC and Web API application targeting .NET 10. It provides stateless in-memory image inspection, resizing, and compression using SkiaSharp (version 4.151.1).

### Defense-in-Depth Model
The system enforces security across multiple distinct layers:
1. **Network & Web Server Layer (Kestrel / Reverse Proxy):** Enforces request body size limits, rate limiting, and security response headers (strict CSP, HSTS, X-Content-Type-Options, X-Frame-Options).
2. **Controller & Model Binding Layer:** Validates HTTP request structures, rejects oversized payloads before buffering, verifies form-data models, and sanitizes untrusted input before logging to prevent CRLF log injection.
3. **Pipeline & Service Validation Layer:** Enforces multi-tier inspection: file extension allowlists, MIME type checks, binary magic byte signature verification, and pre-decode dimension and pixel budget limits.
4. **Image Processing Engine Layer (SkiaSharp):** Safely allocates bitmaps within strict pixel limits, handles native Skia allocations, cleans up native unmanaged pointers, and mitigates integer overflow.
5. **Client Layer (Browser):** Operates under a strict Content Security Policy without `unsafe-inline` or `unsafe-eval` for scripts, cleans up Blob/Object URLs to prevent client-side memory leakage, and supports request cancellation via `AbortController`.

---

## 2. Stateless / Zero-Storage Guarantee

`PixelForge` operates strictly under a **zero persistent image storage** architecture:
- **No Disk Storage:** Uploaded files, resized images, and compressed assets are never written to the local server file system (`File.Write*`, `FileStream`, and temporary directories are not used).
- **In-Memory Request Scope:** Images exist strictly in memory (`MemoryStream`, `byte[]`, and Skia `SKBitmap` / `SKCodec`) for the lifetime of the active HTTP request.
- **Immediate Garbage Collection:** All streams, bitmaps, codecs, and native Skia data buffers implement `IDisposable` and are disposed in `using` or `finally` blocks upon completion of the request.
- **No Database / Cache Persistence:** There are no databases, object stores (S3/Azure Blob), session stores, or file caches holding processed imagery.
- **Client Ownership:** Once the processing completes, the resulting bytes are returned directly in the HTTP response body and held in browser memory as ephemeral `Blob` URLs until downloaded or discarded.

---

## 3. Supported vs Unsupported Image Formats

| Format | Extension Allowlist | Magic Signature Verified | Inspection | Resizing Supported | Compression Supported |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **JPEG / JPG** | `.jpg`, `.jpeg` | `0xFF 0xD8 0xFF` | Yes | Yes (output JPEG/WebP/PNG) | Yes (lossy quality 1–100) |
| **PNG** | `.png` | `0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A` | Yes | Yes (output PNG/JPEG/WebP) | Yes (deflate level 0–9 & smart bloat prevention) |
| **WebP** | `.webp` | `RIFF....WEBP` | Yes | Yes (output WebP/JPEG/PNG) | Yes (lossy quality 1–100) |
| **GIF** | `.gif` | `0x47 0x49 0x46 0x38 (GIF87a / GIF89a)` | Yes | **Rejected** (documented limitation) | **Rejected** (documented limitation) |
| **BMP** | `.bmp` | `0x42 0x4D (BM)` | Yes | Yes (output converted to JPEG/PNG/WebP) | **Rejected** (documented limitation) |
| **TIFF / SVG / AVIF / HEIC / EXE** | *None* | *None* | **Rejected** | **Rejected** | **Rejected** |

- **GIF Limitation:** SkiaSharp does not provide native multi-frame GIF encoding. GIF inputs can be inspected, but resize/compression attempts return a structured validation rejection rather than corrupting animation or crashing native bindings.
- **BMP Compression:** BMP compression is disallowed because uncompressed bitmap encoding offers negligible size reduction; users are directed to resize or output to WebP/PNG/JPEG.
- **SVG / Vector Formats:** SVG is explicitly disallowed to prevent XML External Entity (XXE) and Cross-Site Scripting (XSS) attacks in vector rendering.

---

## 4. Validation Pipeline Details

Every uploaded image must pass a five-stage verification pipeline before SkiaSharp decodes pixel data:

```
[Incoming HTTP Request]
         │
         ▼
[1. File Size Verification]
  ├── file.Length == 0 ──> 400 Bad Request ("File is empty")
  └── file.Length > 20 MB ──> 413 Payload Too Large
         │
         ▼
[2. Extension Verification]
  └── Extension not in [.jpg, .jpeg, .png, .webp, .gif, .bmp] ──> 400 Bad Request
         │
         ▼
[3. Binary Magic Byte Verification]
  └── Stream header signature does not match format ──> 400 Bad Request ("Fake extension / Mismatch")
         │
         ▼
[4. Codec Header Inspection (Pre-Decode)]
  ├── SKCodec.Create(managedStream) parses header only (0 raw pixel allocation)
  ├── Source Dimensions > 10,000 x 10,000 px ──> Rejection
  └── Source Pixels > 50,000,000 px (50 MP) ──> Rejection ("Decompression bomb defense")
         │
         ▼
[5. Parameter & Dimension Calculation Check]
  ├── Target Width/Height <= 0 or > 10,000 px ──> Rejection
  ├── Target Pixels > 50,000,000 px ──> Rejection
  └── Math overflow check on proportional aspect ratios
         │
         ▼
[Safe Execution: Allocate Bitmap & Encode]
```

---

## 5. Dimension, Pixel, and Memory Limits

To protect system resources against denial-of-service and memory exhaustion:
- **Maximum Upload File Size:** `20 MB` (`20,971,520 bytes`).
- **Kestrel / FormOptions Limit:** `20 MB + 512 KB` margin (`22,020,096 bytes`) to accommodate multipart boundaries, headers, and form metadata.
- **Maximum Image Width:** `10,000 pixels`.
- **Maximum Image Height:** `10,000 pixels`.
- **Maximum Input Pixel Budget:** `50,000,000 pixels` (50 Megapixels).
- **Maximum Output Pixel Budget:** `50,000,000 pixels` (50 Megapixels).
- **Integer Overflow Protection:** All dimension products and aspect ratio calculations use `checked((long)width * (long)height)` and explicit checks against `double.IsNaN`, `double.IsInfinity`, and `<= 0` values. `ResizeRequest` models enforce `[Range(1, 10000)]` on user-specified dimensions.

---

## 6. Compression Security & Decompression Bomb Defenses

### Decompression Bomb (Pixel Flood) Defense
Decompression bombs occur when a tiny compressed file (e.g., a 100 KB PNG) expands to gigabytes of raw pixel memory (e.g., 60,000 x 60,000 RGBA pixels).
- `PixelForge` uses `SKCodec.Create()` to read image metadata directly from the container headers *without* allocating an `SKBitmap` or unpacking uncompressed scanlines.
- If the header indicates dimensions greater than `10,000` pixels or total pixels greater than `50 MP`, processing is terminated immediately before full rasterization.

### Smart Bloat Prevention
When re-encoding images (specifically PNGs at maximum zlib compression or pre-optimized images), decoding to RGBA and re-encoding can sometimes increase the file size.
- `ImageCompressionService` compares `candidateBytes.Length < originalBytes.Length`.
- If re-encoding does not yield a smaller file, the original byte stream is retained and returned with a status notice, preventing payload inflation.

---

## 7. Content Security Policy & Browser Protections

The application sends strict HTTP response headers on all responses:

### Content Security Policy (CSP)
```http
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self';
```
- **Zero Inline Scripts:** No `'unsafe-inline'` and no `'unsafe-eval'`. The theme initialization logic is placed in an external script (`/js/theme-init.js`).
- **Clickjacking Protection:** `frame-ancestors 'none'` and `X-Frame-Options: DENY` prevent framing in iframes.
- **Plugin Lockdown:** `object-src 'none'` disallows Flash, Java applets, or PDF embed injection.
- **MIME Sniffing Defense:** `X-Content-Type-Options: nosniff`.
- **Referrer Policy:** `Referrer-Policy: strict-origin-when-cross-origin`.
- **Permissions Policy:** `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()`.

### Client-Side Browser Memory Management
- The frontend tracks all created Object URLs (`URL.createObjectURL`) and explicitly calls `URL.revokeObjectURL()` whenever a new image is loaded, when errors occur, and upon resetting state.
- In-flight fetch requests are linked to `AbortController` instances, canceling network transfers and releasing resources if the user switches files or actions.

---

## 8. Anti-Abuse & Rate Limiting Controls

To protect CPU resources against denial-of-service, all endpoints in `ImageController` (`/api/image/inspect`, `/api/image/resize`, `/api/image/compress`) are governed by ASP.NET Core Rate Limiting:
- **Policy Name:** `ImageProcessingPolicy`.
- **Algorithm:** Fixed Window Limiter keyed by client remote IP address (falls back to `"anonymous"` if behind proxy).
- **Default Limit:** `30 permits per 60-second window` with `QueueLimit = 0` (immediate rejection).
- **Response on Limit Exceeded:** `429 Too Many Requests` with a descriptive error message.
- **Configuration:** Limits are configurable via `RateLimiting` section in `appsettings.json`.

---

## 9. File Sanitization & Path Traversal Prevention

Untrusted filenames sent in `Content-Disposition` headers can introduce directory traversal or operating system reserved name vulnerabilities.

### Sanitization Strategy
Both `ImageResizeService` and `ImageCompressionService` implement `GenerateSafeOutputFileName`:
1. Strips directory paths using `Path.GetFileNameWithoutExtension`.
2. Removes all characters in `Path.GetInvalidFileNameChars()`, slashes (`/`, `\`), colons (`:`), null bytes (`\0`), and control characters.
3. Trims whitespace, periods, and hyphens.
4. If empty or matching Windows reserved device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `COM0`, `LPT1`–`LPT9`, `LPT0`), the base name defaults to `"image"`.
5. Clamps maximum base name length to 64 characters.
6. Appends a controlled suffix and normalized extension (e.g. `image_resized_800x600.png` or `image_compressed.jpg`).

### CRLF Log Injection Defense
Filenames logged via `ILogger` are passed through `SanitizeForLog()`, which replaces newline characters (`\r`, `\n`) and control characters with underscores (`_`) and truncates the string to 100 characters.

---

## 10. Reverse Proxy / Production Deployment Guidelines

When deploying `PixelForge` to production behind a reverse proxy (Nginx, Cloudflare, AWS ALB, IIS):

### Nginx Example
```nginx
server {
    listen 443 ssl http2;
    server_name resize.example.com;

    # Align max upload size (20 MB + margin)
    client_max_body_size 22M;

    # Pass client IP for rate limiting
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Host $host;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
    }
}
```

### Key Recommendations
1. **Request Body Alignment:** Ensure reverse proxy `client_max_body_size` is at least `22 MB` to avoid dropping valid 20 MB multipart requests before they reach Kestrel.
2. **Forwarded Headers Middleware:** In ASP.NET Core, configure `UseForwardedHeaders` if rate limiting by client IP behind a reverse proxy.
3. **HTTPS Redirection & HSTS:** Enforce TLS 1.3/1.2; enable HSTS headers at the edge or via ASP.NET Core `UseHsts()`.
4. **Server Header Suppression:** Kestrel server header is disabled via `AddServerHeader = false`. Ensure reverse proxies also strip `Server` headers.

---

## 11. Vulnerability Reporting Policy

We take the security of `PixelForge` seriously. If you discover a security vulnerability, please report it responsibly:

- **Email:** Open a private security advisory via GitHub Security Advisories or contact the maintainers directly.
- **Include in Report:**
  - Description of the vulnerability and attack vector.
  - Step-by-step reproduction instructions or proof-of-concept payload.
  - Impact assessment.
  - Potential remediation suggestions (if available).

Please do not disclose security vulnerabilities publicly until they have been reviewed and resolved.

---

## 12. Known Limitations & Non-Goals

The following features are **explicit non-goals** of `PixelForge`:
- **User Authentication / Accounts:** The tool is designed as a public, stateless utility.
- **Persistent Storage / Cloud Uploads:** No user images will ever be saved to disk, cloud buckets, or databases.
- **Arbitrary Format Conversion:** SVG, TIFF, and RAW formats are not supported.
- **Multi-Frame Animated GIF Processing:** Not supported due to native encoding library limitations.
- **Batch Processing / Asynchronous Queues:** Requests are processed synchronously per HTTP call.

---

## 13. Threat Model

| Threat Actor / Vector | Attack Description | Mitigation in PixelForge |
| :--- | :--- | :--- |
| **Malicious File Upload** | Attacker uploads executable (.exe, .sh) or malicious script disguised as image. | Extension allowlist, MIME validation, binary magic header validation. Executables never executed; zero disk storage. |
| **Decompression Bomb / Pixel Flood** | Attacker uploads small image that decompresses to billions of pixels in memory. | Codec pre-decode dimension and 50 MP pixel limits reject file prior to bitmap allocation. |
| **Integer Overflow Exploitation** | Attacker specifies huge dimensions (e.g. `2,000,000,000`) causing integer wrapping to negative values. | Model validation `[Range(1, 10000)]`, service-layer bounds checks, and `checked` arithmetic. |
| **Path Traversal / Overwrite** | Attacker uploads `../../evil.png` or `CON.png` to manipulate file system or crash OS. | No disk writes exist; `GenerateSafeOutputFileName` strips traversal tokens and Windows device names. |
| **Denial-of-Service / CPU Starvation** | Attacker spams heavy resizing or compression requests to exhaust CPU. | Fixed-window rate limiter (30 req/min per IP), Kestrel request limits, cancellation tokens on client disconnect. |
| **Cross-Site Scripting (XSS)** | Attacker attempts script injection via image payload or filenames. | Strict Content Security Policy (`script-src 'self'`, no inline scripts), SVG rejection, DOM text content sanitization. |
| **Log Injection / Forging** | Attacker sends `\r\n[CRITICAL] Admin logged in` in filename header. | `SanitizeForLog` strips control characters, carriage returns, and line feeds before logging. |

---

## 14. SkiaSharp / Native Library Security Considerations

- **Native Binary Execution:** SkiaSharp relies on precompiled native binaries (`libSkiaSharp`). Ensure runtime dependencies and underlying native libraries (`libSkiaSharp.dll` / `libSkiaSharp.so`) are sourced strictly from official NuGet packages (`SkiaSharp` and `SkiaSharp.NativeAssets.*`).
- **Memory Management:** SkiaSharp objects wrap unmanaged C++ pointers. All `SKBitmap`, `SKCodec`, `SKImage`, `SKData`, and `SKManagedStream` objects are deterministically disposed within `using` statements to prevent unmanaged memory leaks.
- **No Unsafe Code:** The C# codebase does not compile with `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.

---

## 15. Incident Response / Triage Process

1. **Receipt & Acknowledgement:** Security reports will be acknowledged within 48 hours of receipt.
2. **Assessment & Reproduction:** The engineering team will reproduce the issue in an isolated test harness and evaluate severity (CVSS score).
3. **Patch Development:** A fix will be developed in a private security branch and verified with unit and regression tests.
4. **Release:** An updated patch release will be tagged and deployed.
5. **Advisory Disclosure:** A GitHub Security Advisory will be published summarizing the vulnerability and credited reporters.

---

## 16. Responsible Disclosure Timeline

- **Day 0:** Vulnerability reported by security researcher.
- **Day 1–2:** Maintainer acknowledges report and begins triage.
- **Day 3–7:** Vulnerability confirmed, reproduction verified, patch created and tested.
- **Day 8–14:** Release deployment and public disclosure coordination.

