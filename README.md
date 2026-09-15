# PixelForge

**Resize. Compress. Keep your pixels.**

A modern, high-performance, web-based image processing tool built with **ASP.NET Core MVC** and **SkiaSharp** on **.NET 10**.

PixelForge allows users to upload images, inspect their technical metadata, scale dimensions up or down with strict aspect-ratio preservation, compress images with zero dimension alteration, compare before-and-after results, and download processed images—all with zero persistent server-side image storage and zero external paid APIs.

---

## Key Features

- **Aspect Ratio Preservation**:
  - Automatically calculates reciprocal dimensions when either width or height is adjusted.
  - Optional lock/unlock toggle to support free-form custom stretching when needed.
  - Formatted aspect ratio badges (e.g., `16:9`, `4:3`, `1:1`, `21:9`).

- **Image Compression (Dimensions Invariant)**:
  - **Zero Dimension Changes**: Preserves width, height, and aspect ratio strictly (1:1 original resolution).
  - **Supported Formats**: JPEG (`.jpg`, `.jpeg`), PNG (`.png`), WebP (`.webp`).
  - **Lossy JPEG Compression**: DCT quantization with standard 4:2:0 chroma subsampling and quality slider (`1%`–`100%`, default `75%`), stripping redundant EXIF metadata for byte optimization.
  - **Lossy WebP Compression**: VP8 lossy compression (`1%`–`100%`, default `75%`) with alpha transparency preservation.
  - **Lossless PNG Optimization**: Strict lossless deflate effort (`ZLibLevel` `0`–`9`, default `9`) with all row prediction filters; guarantees 100% pixel fidelity.
  - **Format Conversion Prohibited**: Output format strictly matches input format. GIF and BMP compression are cleanly rejected with clear user-facing messages rather than silently converted.
  - **First-Class Metrics**: Reports original file size, compressed file size, bytes saved, and reduction percentage. If an already-optimized file becomes larger, a clear warning badge is displayed.

- **Dual Resizing Modes**:
  - **By Percentage**: Scale from `1%` to `500%` using an interactive slider, numeric input, or quick presets (`25%`, `50%`, `75%`, `100%`, `200%`, `400%`).
  - **Custom Dimensions**: Specify exact target width or height up to `10,000 px`.

- **Multi-Format Support**:
  - **Supported Inputs**: JPEG (`.jpg`, `.jpeg`), PNG (`.png`), WebP (`.webp`), GIF (`.gif`), BMP (`.bmp`).
  - **Supported Outputs (Resize)**: JPEG, PNG, WebP.
  - **Smart Fallback (Resize)**: GIF and BMP inputs are safely accepted and automatically routed to lossless PNG encoding.
  - **Quality Control**: Adjustable quality slider (`1%`–`100%`) for JPEG and WebP; lossless indicator for PNG.
  - **Transparency Handling**: When converting transparent images (PNG/WebP) to JPEG, the alpha channel is composited cleanly over a solid white background.

- **In-Application API Rate Limiting**:
  - **Policy**: Partitioned per-client IP fixed-window rate limiter using ASP.NET Core `Microsoft.AspNetCore.RateLimiting`.
  - **Default Limits**: 30 requests per 60 seconds window with 0 request queue depth to protect CPU/memory from burst exhaustion.
  - **Structured 429 Response**: Returns standard `429 Too Many Requests` HTTP status, RFC 7231 `Retry-After` header, and structured JSON body (`{ "success": false, "errorMessage": "..." }`).
  - **Frontend UX**: Automatically catches HTTP 429 errors and displays non-blocking warning alerts indicating retry wait duration.

- **Enterprise-Grade Security & Validation**:
  - **Magic-Byte Signature Verification**: Validates actual binary file headers; rejects spoofed extensions (e.g., text or script files renamed to `.jpg`).
  - **Decompression Bomb Protection**: Enforces maximum dimension (`10,000 px`) and maximum pixel thresholds (`50,000,000 px`) prior to allocating memory.
  - **Strict Size Limits**: Enforces a `20 MB` request ceiling across Kestrel server limits, form multipart limits, and service-layer streams.
  - **Path-Traversal Sanitization**: Strips dangerous path sequences (`../`, `\`, `:`) to ensure safe output filenames.

- **Privacy & Memory Architecture**:
  - **Zero Persistent Storage**: Uploaded and processed images are processed exclusively in-memory and never written to server disk or database.
  - **Browser Memory Lifecycle**: Object URLs (`blob:`) are revoked automatically upon file change or reset to prevent client memory leaks.
  - **Concurrency Resilience**: Equipped with `AbortController` guards to cancel obsolete in-flight requests when switching files rapidly.

---

## Technology Stack

- **Framework**: ASP.NET Core MVC (.NET 10, C# 13)
- **Image Processing Engine**: SkiaSharp 4.151.1 (High-fidelity Catmull-Rom cubic resampling for resize; native encoder options for compression)
- **Native Runtime Assets**: `SkiaSharp.NativeAssets.Win32` (Windows) & `SkiaSharp.NativeAssets.Linux.NoDependencies` (Linux/Docker)
- **Frontend**: Bootstrap 5 + Semantic HTML5 + Vanilla JavaScript (ES6+)
- **Testing**: xUnit with 111 automated unit, integration, and security tests
- **Benchmarking**: BenchmarkDotNet 0.14.0 harness for reproducible performance profiling

---

## Project Structure

```
PixelForge/
│
├── ImageResizeTool/                     # Main Web Application (ImageResizeTool.csproj)
│   ├── Configuration/
│   │   ├── ImageProcessingOptions.cs    # Image size, dimension, and format options
│   │   └── RateLimitingOptions.cs       # Rate limiter window, permit, and queue configuration
│   ├── Controllers/
│   │   ├── HomeController.cs            # MVC View Controller (Index, Privacy, Error)
│   │   └── ImageController.cs           # REST API Controller (/api/image/*) with [EnableRateLimiting]
│   ├── Exceptions/
│   │   └── ImageValidationException.cs  # Safe, client-facing domain exception
│   ├── Models/
│   │   ├── ImageMetadata.cs             # Technical image metadata model
│   │   ├── ImageInspectResult.cs        # Response model for inspection
│   │   ├── ResizeMode.cs                # Enum: Percentage | CustomDimensions
│   │   ├── ResizeRequest.cs             # Multipart request model for resize
│   │   ├── ResizeResult.cs              # Response model for resize
│   │   ├── CompressRequest.cs           # Multipart request model for compression
│   │   └── CompressResult.cs            # Response model for compression
│   ├── Services/
│   │   ├── Interfaces/
│   │   │   ├── IImageResizeService.cs   # Service contract for resizing
│   │   │   └── IImageCompressionService.cs # Service contract for compression
│   │   ├── ImageResizeService.cs        # Core SkiaSharp validation and resize engine
│   │   └── ImageCompressionService.cs   # Native SkiaSharp compression engine
│   ├── Views/
│   │   ├── Home/
│   │   │   ├── Index.cshtml             # Resizer and Compressor UI
│   │   │   └── Privacy.cshtml           # Factual Terms & Privacy document
│   │   └── Shared/
│   │       ├── _Layout.cshtml           # Master layout with navigation and theme toggle
│   │       └── _ValidationScriptsPartial.cshtml
│   ├── wwwroot/
│   │   ├── css/site.css                 # Custom styling, dropzone, and responsive layout
│   │   └── js/site.js                   # Client state machine, AbortController, preview, and download
│   ├── appsettings.json                 # Image processing options & rate limiting configuration
│   └── Program.cs                       # Rate limiter, DI, Kestrel limits, and pipeline setup
│
├── ImageResizeTool.Tests/               # Automated Test Suite (ImageResizeTool.Tests.csproj)
│   ├── AspectRatioTests.cs              # Math, scaling, and aspect ratio unit tests
│   ├── SecurityAndValidationTests.cs    # Binary signature, limits, and security tests
│   ├── ResizeEngineTests.cs             # Resampling, format conversion, and filename tests
│   ├── ImageControllerTests.cs          # API controller resize integration tests
│   ├── CompressionEngineTests.cs        # Format-specific compression, fidelity, and invariance tests
│   ├── CompressionControllerTests.cs    # API controller compression integration tests
│   ├── RateLimitingTests.cs             # In-app rate limiting and HTTP 429 integration tests
│   └── SecurityHardeningTests.cs        # CSP, integer overflow, reserved device names, and traversal tests
│
├── ImageResizeTool.Benchmarks/          # BenchmarkDotNet Performance Suite (ImageResizeTool.Benchmarks.csproj)
│   ├── ImageProcessingBenchmarks.cs     # Synthetic benchmark scenarios (800x600, 1080p, 4K across JPEG/PNG/WebP)
│   └── Program.cs                       # BenchmarkDotNet runner entrypoint
│
└── ImageResizeTool.slnx                 # Visual Studio XML solution file
```

---

## Configuration

Settings can be customized in `ImageResizeTool/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ImageProcessing": {
    "MaxFileSizeBytes": 20971520,
    "MaxInputWidth": 10000,
    "MaxInputHeight": 10000,
    "MaxInputPixels": 50000000,
    "MaxOutputWidth": 10000,
    "MaxOutputHeight": 10000,
    "AllowedExtensions": [ ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" ],
    "DefaultJpegQuality": 85,
    "DefaultWebpQuality": 85
  },
  "RateLimiting": {
    "PermitLimit": 30,
    "WindowSeconds": 60,
    "QueueLimit": 0
  }
}
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Running via .NET CLI

1. Clone or open the repository folder:
   ```bash
   cd ImageResizeTool
   ```

2. Restore dependencies and build the solution:
   ```bash
   dotnet build
   ```

3. Run the web application:
   ```bash
   dotnet run --project ImageResizeTool/ImageResizeTool.csproj
   ```

4. Open your browser and navigate to the displayed localhost URL (e.g., `https://localhost:7084`).

### Running in Visual Studio

1. Open `ImageResizeTool.slnx` in Visual Studio 2022 / 2026.
2. Select `ImageResizeTool` as the startup project.
3. Press **F5** (Debug) or **Ctrl+F5** (Run without debugging).

---

## Running Automated Tests

Run the complete test suite:

```bash
dotnet test
```

---

## Performance Benchmarking

A standalone benchmark harness using **BenchmarkDotNet** is included in `ImageResizeTool.Benchmarks`. It executes deterministic synthetic workloads across small (800x600), medium (1920x1080), and large (4000x3000) image resolutions for JPEG, PNG, and WebP resizing and compression without requiring external asset files.

To execute the benchmarks:

```bash
dotnet run -c Release --project ImageResizeTool.Benchmarks/ImageResizeTool.Benchmarks.csproj
```

---

## REST API Reference

### 1. Inspect Image
- **Endpoint**: `POST /api/image/inspect`
- **Content-Type**: `multipart/form-data`
- **Body**: `file` (binary image stream)
- **Response**: `ImageInspectResult` JSON containing dimensions, aspect ratio, format, MIME type, and file size.

### 2. Resize Image
- **Endpoint**: `POST /api/image/resize`
- **Content-Type**: `multipart/form-data`
- **Parameters**:
  - `file`: Image file stream
  - `mode`: `Percentage` | `CustomDimensions`
  - `percentage`: Number (`1.0`–`500.0`, required if `mode=Percentage`)
  - `targetWidth`: Target width in pixels (optional if `mode=CustomDimensions`)
  - `targetHeight`: Target height in pixels (optional if `mode=CustomDimensions`)
  - `preserveAspectRatio`: Boolean (`true` | `false`)
  - `targetFormat`: `jpeg` | `png` | `webp`
  - `quality`: Integer (`1`–`100`)
- **Response**: `ResizeResult` JSON containing Base64 encoded `processedBytes`, `suggestedFileName`, `mimeType`, and before/after metadata.

### 3. Compress Image (Dimensions Invariant)
- **Endpoint**: `POST /api/image/compress`
- **Content-Type**: `multipart/form-data`
- **Parameters**:
  - `file`: Image file stream (JPEG, PNG, or WebP)
  - `quality`: Integer (`1`–`100`, applies to lossy JPEG and WebP; default `75`)
  - `pngCompressionLevel`: Integer (`0`–`9`, applies to lossless PNG deflate effort; default `9`)
- **Behavior**:
  - Output width and height remain strictly identical to source dimensions.
  - Output format strictly matches source format (conversion prohibited).
  - GIF and BMP requests return `400 Bad Request` with an explanatory message.
- **Response**: `CompressResult` JSON containing Base64 encoded `processedBytes`, `suggestedFileName`, `mimeType`, before/after metadata, `originalFileSizeBytes`, `compressedFileSizeBytes`, `savedBytes`, `reductionPercentage`, and `isLargerThanOriginal`.

---

## Production Deployment Considerations

1. **Reverse Proxy Configuration**:
   When deploying behind reverse proxies such as Nginx, Apache, Cloudflare, IIS, or AWS ALB, ensure the maximum client body size is configured to match or exceed `20 MB` (e.g., `client_max_body_size 20M;` in Nginx). Ensure upstream proxy forwards `X-Forwarded-For` headers if client IP partitioning is desired.

2. **Linux & Container Deployments**:
   The project references `SkiaSharp.NativeAssets.Linux.NoDependencies` alongside `SkiaSharp.NativeAssets.Win32`, allowing direct containerization on Linux (`mcr.microsoft.com/dotnet/aspnet:10.0`) without requiring manual fontconfig or freetype system package installation.

3. **Stateless Horizontal Scaling**:
   Because PixelForge operates purely in-memory without persistent local storage or server sessions, any number of application instances can be scaled horizontally behind a load balancer with zero shared storage requirements.

---

## License

This project is licensed under the MIT License.

