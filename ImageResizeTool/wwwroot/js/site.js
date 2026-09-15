// PixelForge - Client-side UI, Theme Toggle, Inspection, Resizing, Preview, and Download

// -------------------------------------------------------------
// Theme Management System
// -------------------------------------------------------------
function initThemeSwitcher() {
    const themeToggleBtn = document.getElementById("themeToggleBtn");
    
    function getPreferredTheme() {
        const storedTheme = localStorage.getItem("pixelforge_theme") || localStorage.getItem("irt_theme");
        if (storedTheme === "dark" || storedTheme === "light") {
            return storedTheme;
        }
        return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-bs-theme", theme);
        document.documentElement.setAttribute("data-theme", theme);
        
        if (themeToggleBtn) {
            themeToggleBtn.setAttribute("aria-label", theme === "dark" ? "Switch to light theme" : "Switch to dark theme");
            themeToggleBtn.setAttribute("title", theme === "dark" ? "Switch to light theme" : "Switch to dark theme");
        }
    }

    // Initialize theme state
    const currentTheme = getPreferredTheme();
    applyTheme(currentTheme);

    if (themeToggleBtn) {
        themeToggleBtn.addEventListener("click", () => {
            const activeTheme = document.documentElement.getAttribute("data-bs-theme") || "light";
            const newTheme = activeTheme === "dark" ? "light" : "dark";
            localStorage.setItem("pixelforge_theme", newTheme);
            applyTheme(newTheme);
        });
    }

    // Synchronize if system color scheme changes and user hasn't set explicit preference
    try {
        const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
        mediaQuery.addEventListener("change", (e) => {
            if (!localStorage.getItem("pixelforge_theme") && !localStorage.getItem("irt_theme")) {
                const sysTheme = e.matches ? "dark" : "light";
                applyTheme(sysTheme);
            }
        });
    } catch (err) {
        console.warn("Media query listener not supported:", err);
    }
}

document.addEventListener("DOMContentLoaded", () => {
    // Initialize Theme Switcher on all pages
    initThemeSwitcher();

    // Guard against running editor script on non-editor pages (e.g. Privacy page)
    const uploadDropzone = document.getElementById("uploadDropzone");
    if (!uploadDropzone) {
        return;
    }

    // -------------------------------------------------------------
    // Application State
    // -------------------------------------------------------------
    const state = {
        selectedFile: null,
        originalPreviewUrl: null,
        processedPreviewUrl: null,
        originalMetadata: null,
        outputMetadata: null,
        suggestedFileName: null,
        outputMimeType: null,
        inspectionSuccessful: false,
        resultIsCurrent: false,
        currentToolMode: "Resize", // "Resize" or "Compress"
        resizeMode: "Percentage", // "Percentage" or "CustomDimensions"
        percentage: 100,
        targetWidth: null,
        targetHeight: null,
        preserveAspectRatio: true,
        quality: 85,
        targetFormat: "jpeg", // "jpeg", "png", "webp"
        compressQuality: 75,
        pngCompressionLevel: 9,
        isInspecting: false,
        isProcessing: false,
        isUpdatingDimensions: false, // guard against mutual event loops
        inspectAbortController: null,
        resizeAbortController: null,
        compressAbortController: null
    };

    const MAX_FILE_SIZE = 20 * 1024 * 1024; // 20 MB
    const ALLOWED_EXTENSIONS = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"];
    const MAX_DIMENSION = 10000;

    // -------------------------------------------------------------
    // DOM Elements
    // -------------------------------------------------------------
    const imageFileInput = document.getElementById("imageFileInput");
    const browseBtn = document.getElementById("browseBtn");
    const statusAlert = document.getElementById("statusAlert");
    const inspectingIndicator = document.getElementById("inspectingIndicator");

    const previewCard = document.getElementById("previewCard");
    const originalImagePreview = document.getElementById("originalImagePreview");

    const metadataCard = document.getElementById("metadataCard");
    const metaFileName = document.getElementById("metaFileName");
    const metaFormat = document.getElementById("metaFormat");
    const metaDimensions = document.getElementById("metaDimensions");
    const metaAspectRatio = document.getElementById("metaAspectRatio");
    const metaFileSize = document.getElementById("metaFileSize");

    const controlsLockBadge = document.getElementById("controlsLockBadge");
    const modePercentageRadio = document.getElementById("modePercentage");
    const modeCustomRadio = document.getElementById("modeCustom");
    const percentageSection = document.getElementById("percentageSection");
    const customDimensionsSection = document.getElementById("customDimensionsSection");

    const percentageInput = document.getElementById("percentageInput");
    const percentageSlider = document.getElementById("percentageSlider");
    const presetButtons = document.querySelectorAll(".preset-btn");

    const aspectRatioLockToggle = document.getElementById("aspectRatioLockToggle");
    const targetWidthInput = document.getElementById("targetWidthInput");
    const targetHeightInput = document.getElementById("targetHeightInput");

    const outputFormatSelect = document.getElementById("outputFormatSelect");
    const formatNotice = document.getElementById("formatNotice");
    const qualitySection = document.getElementById("qualitySection");
    const qualitySlider = document.getElementById("qualitySlider");
    const qualityValueBadge = document.getElementById("qualityValueBadge");
    const qualityNotice = document.getElementById("qualityNotice");

    const summaryDimensions = document.getElementById("summaryDimensions");
    const summaryAspectRatio = document.getElementById("summaryAspectRatio");
    const summaryFormat = document.getElementById("summaryFormat");

    const readyBanner = document.getElementById("readyBanner");
    const resizeBtn = document.getElementById("resizeBtn");
    const resetBtn = document.getElementById("resetBtn");

    // Initial State & Mode Switcher Elements
    const initialStatePrompt = document.getElementById("initialStatePrompt");
    const toolModeCard = document.getElementById("toolModeCard");
    const tabResizeMode = document.getElementById("tabResizeMode");
    const tabCompressMode = document.getElementById("tabCompressMode");
    const controlsCard = document.getElementById("controlsCard");
    const compressControlsCard = document.getElementById("compressControlsCard");

    // Compression Controls Elements
    const compressOrigDimensions = document.getElementById("compressOrigDimensions");
    const compressOrigFormat = document.getElementById("compressOrigFormat");
    const compressOrigSize = document.getElementById("compressOrigSize");
    const compressQualitySection = document.getElementById("compressQualitySection");
    const compressQualitySlider = document.getElementById("compressQualitySlider");
    const compressQualityBadge = document.getElementById("compressQualityBadge");
    const compressPresetBtns = document.querySelectorAll(".compress-preset-btn");
    const compressPngSection = document.getElementById("compressPngSection");
    const compressPngBtns = document.querySelectorAll(".compress-png-btn");
    const compressUnsupportedSection = document.getElementById("compressUnsupportedSection");
    const compressTargetDimensions = document.getElementById("compressTargetDimensions");
    const compressTargetFormat = document.getElementById("compressTargetFormat");
    const compressReadyBanner = document.getElementById("compressReadyBanner");
    const compressBtn = document.getElementById("compressBtn");
    const compressResetBtn = document.getElementById("compressResetBtn");

    // Result Section Elements
    const resultSection = document.getElementById("resultSection");
    const resultCardTitle = document.getElementById("resultCardTitle");
    const staleBadge = document.getElementById("staleBadge");
    const staleNotice = document.getElementById("staleNotice");
    const downloadBtn = document.getElementById("downloadBtn");
    const uploadAnotherBtn = document.getElementById("uploadAnotherBtn");

    const sizeComparisonText = document.getElementById("sizeComparisonText");
    const sizeDeltaBadge = document.getElementById("sizeDeltaBadge");

    const resultOriginalDimensionsBadge = document.getElementById("resultOriginalDimensionsBadge");
    const resultOriginalImage = document.getElementById("resultOriginalImage");
    const resultOriginalFormat = document.getElementById("resultOriginalFormat");
    const resultOriginalRatio = document.getElementById("resultOriginalRatio");
    const resultOriginalSize = document.getElementById("resultOriginalSize");

    const resultOutputColumnTitle = document.getElementById("resultOutputColumnTitle");
    const resultOutputDimensionsBadge = document.getElementById("resultOutputDimensionsBadge");
    const resultProcessedImage = document.getElementById("resultProcessedImage");
    const resultOutputFormat = document.getElementById("resultOutputFormat");
    const resultOutputRatio = document.getElementById("resultOutputRatio");
    const resultOutputSize = document.getElementById("resultOutputSize");

    // -------------------------------------------------------------
    // Helper Utilities
    // -------------------------------------------------------------
    function formatBytes(bytes) {
        if (!bytes || bytes <= 0) return "0 Bytes";
        const k = 1024;
        const sizes = ["Bytes", "KB", "MB", "GB"];
        const i = Math.min(Math.floor(Math.log(bytes) / Math.log(k)), sizes.length - 1);
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + " " + sizes[i];
    }

    function showAlert(message, type = "danger") {
        statusAlert.className = `alert alert-${type} mb-4`;
        statusAlert.textContent = message;
        statusAlert.classList.remove("d-none");
    }

    function clearAlert() {
        statusAlert.classList.add("d-none");
        statusAlert.textContent = "";
    }

    function calculateReducedRatio(width, height) {
        if (!width || !height || width <= 0 || height <= 0) return "—";
        function gcd(a, b) {
            return b === 0 ? a : gcd(b, a % b);
        }
        const divisor = gcd(width, height);
        const rw = width / divisor;
        const rh = height / divisor;
        if (rw <= 25 && rh <= 25) {
            return `${rw}:${rh}`;
        }
        const ratio = (width / height).toFixed(2);
        return `${ratio}:1`;
    }

    function base64ToBlob(base64, mimeType) {
        const byteCharacters = atob(base64);
        const byteNumbers = new Uint8Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        return new Blob([byteNumbers], { type: mimeType });
    }

    function markResultStale() {
        if (resultSection && !resultSection.classList.contains("d-none")) {
            state.resultIsCurrent = false;
            staleBadge.classList.remove("d-none");
            staleNotice.textContent = state.currentToolMode === "Compress"
                ? "Settings changed. Compress again to update the result."
                : "Settings changed. Resize again to update the result.";
            staleNotice.classList.remove("d-none");
        }
    }

    // -------------------------------------------------------------
    // Upload & Drag-and-Drop Event Handlers
    // -------------------------------------------------------------
    browseBtn.addEventListener("click", (e) => {
        e.stopPropagation();
        imageFileInput.click();
    });

    uploadDropzone.addEventListener("click", () => {
        imageFileInput.click();
    });

    uploadDropzone.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            imageFileInput.click();
        }
    });

    let dragCounter = 0;

    uploadDropzone.addEventListener("dragenter", (e) => {
        e.preventDefault();
        e.stopPropagation();
        dragCounter++;
        uploadDropzone.classList.add("dragover");
    });

    uploadDropzone.addEventListener("dragover", (e) => {
        e.preventDefault();
        e.stopPropagation();
    });

    uploadDropzone.addEventListener("dragleave", (e) => {
        e.preventDefault();
        e.stopPropagation();
        dragCounter--;
        if (dragCounter <= 0) {
            dragCounter = 0;
            uploadDropzone.classList.remove("dragover");
        }
    });

    uploadDropzone.addEventListener("drop", (e) => {
        e.preventDefault();
        e.stopPropagation();
        dragCounter = 0;
        uploadDropzone.classList.remove("dragover");

        if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
            handleFileSelection(e.dataTransfer.files[0]);
        }
    });

    imageFileInput.addEventListener("change", (e) => {
        if (e.target.files && e.target.files.length > 0) {
            handleFileSelection(e.target.files[0]);
        }
    });

    // -------------------------------------------------------------
    // File Selection & Local Preview
    // -------------------------------------------------------------
    function handleFileSelection(file) {
        clearAlert();
        readyBanner.classList.add("d-none");
        resultSection.classList.add("d-none");

        // Cancel any pending network requests from a prior file
        if (state.inspectAbortController) {
            state.inspectAbortController.abort();
            state.inspectAbortController = null;
        }
        if (state.resizeAbortController) {
            state.resizeAbortController.abort();
            state.resizeAbortController = null;
        }
        if (state.compressAbortController) {
            state.compressAbortController.abort();
            state.compressAbortController = null;
        }

        if (!file) return;

        // Preliminary validation: Extension
        const fileName = file.name || "";
        const dotIndex = fileName.lastIndexOf(".");
        const extension = dotIndex !== -1 ? fileName.substring(dotIndex).toLowerCase() : "";

        if (!ALLOWED_EXTENSIONS.includes(extension)) {
            showAlert(`The file extension '${extension}' is not supported. Please select a JPG, PNG, WebP, GIF, or BMP image.`);
            resetState();
            return;
        }

        // Preliminary validation: File size
        if (file.size > MAX_FILE_SIZE) {
            showAlert(`The selected file is too large (${formatBytes(file.size)}). The maximum allowed size is 20 MB.`);
            resetState();
            return;
        }

        if (file.size === 0) {
            showAlert("The selected file is empty (0 bytes).");
            resetState();
            return;
        }

        // Revoke previous object URLs to prevent browser memory leaks
        if (state.originalPreviewUrl) {
            URL.revokeObjectURL(state.originalPreviewUrl);
            state.originalPreviewUrl = null;
        }
        if (state.processedPreviewUrl) {
            URL.revokeObjectURL(state.processedPreviewUrl);
            state.processedPreviewUrl = null;
        }

        // Set state
        state.selectedFile = file;
        state.originalPreviewUrl = URL.createObjectURL(file);

        // Immediately show local preview
        originalImagePreview.src = state.originalPreviewUrl;
        originalImagePreview.alt = `Preview of ${file.name}`;
        previewCard.classList.remove("d-none");

        // Call backend inspection
        inspectImageOnServer(file);
    }

    // -------------------------------------------------------------
    // Server Inspection API Integration
    // -------------------------------------------------------------
    async function inspectImageOnServer(file) {
        if (state.inspectAbortController) {
            state.inspectAbortController.abort();
            state.inspectAbortController = null;
        }

        const controller = new AbortController();
        state.inspectAbortController = controller;

        state.isInspecting = true;
        inspectingIndicator.classList.remove("d-none");
        metadataCard.classList.add("d-none");
        resizeBtn.disabled = true;
        compressBtn.disabled = true;

        const formData = new FormData();
        formData.append("file", file);

        try {
            const response = await fetch("/api/image/inspect", {
                method: "POST",
                body: formData,
                signal: controller.signal
            });

            let data;
            try {
                data = await response.json();
            } catch {
                data = null;
            }

            if (response.ok && data && data.success && data.metadata) {
                state.inspectionSuccessful = true;
                state.originalMetadata = data.metadata;

                // Populate Metadata Card
                metaFileName.textContent = file.name;
                metaFormat.textContent = data.metadata.format;
                metaDimensions.textContent = `${data.metadata.width} × ${data.metadata.height} px`;
                metaAspectRatio.textContent = data.metadata.aspectRatioFormatted || `${data.metadata.width}:${data.metadata.height}`;
                metaFileSize.textContent = formatBytes(data.metadata.fileSizeBytes || file.size);

                metadataCard.classList.remove("d-none");

                // Configure default format based on source format
                setupDefaultOutputFormat(data.metadata.format);

                // Initialize dimensions
                initDimensionsFromMetadata();

                // Reveal controls and hide initial empty state prompt
                if (initialStatePrompt) initialStatePrompt.classList.add("d-none");
                if (toolModeCard) toolModeCard.classList.remove("d-none");
                if (state.currentToolMode === "Resize") {
                    controlsCard.classList.remove("d-none");
                    compressControlsCard.classList.add("d-none");
                } else {
                    controlsCard.classList.add("d-none");
                    compressControlsCard.classList.remove("d-none");
                }

                // Enable resize action
                resizeBtn.disabled = false;
            } else {
                state.inspectionSuccessful = false;
                state.originalMetadata = null;
                if (initialStatePrompt) initialStatePrompt.classList.remove("d-none");
                if (toolModeCard) toolModeCard.classList.add("d-none");
                controlsCard.classList.add("d-none");
                compressControlsCard.classList.add("d-none");
                let message = data?.errorMessage;
                if (!message) {
                    if (response.status === 429) {
                        const retryAfter = response.headers.get("Retry-After");
                        message = retryAfter 
                            ? `You're processing images too quickly. Please wait ${retryAfter} seconds and try again.`
                            : "You're processing images too quickly. Please wait a moment and try again.";
                    } else if (response.status === 413) {
                        message = "The image is too large to inspect.";
                    } else if (response.status === 500) {
                        message = "An internal server error occurred while inspecting the image.";
                    } else {
                        message = `Inspection failed (HTTP ${response.status}). The uploaded image could not be inspected.`;
                    }
                }
                showAlert(message);
                resizeBtn.disabled = true;
                compressBtn.disabled = true;
            }
        } catch (error) {
            if (error.name === "AbortError") {
                // Superseded by newer upload or reset
                return;
            }
            state.inspectionSuccessful = false;
            state.originalMetadata = null;
            if (initialStatePrompt) initialStatePrompt.classList.remove("d-none");
            if (toolModeCard) toolModeCard.classList.add("d-none");
            controlsCard.classList.add("d-none");
            compressControlsCard.classList.add("d-none");
            showAlert("Failed to connect to the server for image inspection. Please check your connection and try again.");
            resizeBtn.disabled = true;
            compressBtn.disabled = true;
        } finally {
            if (state.inspectAbortController === controller) {
                state.inspectAbortController = null;
                state.isInspecting = false;
                inspectingIndicator.classList.add("d-none");
            }
        }
    }

    // -------------------------------------------------------------
    // Output Format & Quality Setup
    // -------------------------------------------------------------
    function setupDefaultOutputFormat(sourceFormat) {
        const fmt = (sourceFormat || "").toUpperCase();

        if (fmt === "JPEG" || fmt === "JPG") {
            state.targetFormat = "jpeg";
            outputFormatSelect.value = "jpeg";
            formatNotice.textContent = "";
        } else if (fmt === "PNG") {
            state.targetFormat = "png";
            outputFormatSelect.value = "png";
            formatNotice.textContent = "";
        } else if (fmt === "WEBP") {
            state.targetFormat = "webp";
            outputFormatSelect.value = "webp";
            formatNotice.textContent = "";
        } else if (fmt === "GIF" || fmt === "BMP") {
            // GIF and BMP encoding is not supported by SkiaSharp; default to PNG
            state.targetFormat = "png";
            outputFormatSelect.value = "png";
            formatNotice.textContent = `Source format is ${fmt}. Automatically set output to PNG for safe lossless processing.`;
        } else {
            state.targetFormat = "jpeg";
            outputFormatSelect.value = "jpeg";
            formatNotice.textContent = "";
        }

        updateQualityControlState();
    }

    outputFormatSelect.addEventListener("change", (e) => {
        state.targetFormat = e.target.value;
        if (state.originalMetadata) {
            const src = (state.originalMetadata.format || "").toUpperCase();
            if ((src === "GIF" || src === "BMP") && state.targetFormat === "png") {
                formatNotice.textContent = `Source format is ${src}. Automatically set output to PNG for safe lossless processing.`;
            } else {
                formatNotice.textContent = "";
            }
        }
        updateQualityControlState();
        updateSummary();
        markResultStale();
    });

    function updateQualityControlState() {
        if (state.targetFormat === "png") {
            qualitySection.classList.add("quality-disabled");
            qualitySlider.disabled = true;
            qualityValueBadge.textContent = "Lossless";
            qualityNotice.textContent = "PNG uses lossless compression (quality setting does not apply).";
        } else {
            qualitySection.classList.remove("quality-disabled");
            qualitySlider.disabled = false;
            qualityValueBadge.textContent = `${state.quality}%`;
            qualityNotice.textContent = "Higher quality produces better images with larger file sizes.";
        }
    }

    qualitySlider.addEventListener("input", (e) => {
        state.quality = parseInt(e.target.value, 10);
        if (state.targetFormat !== "png") {
            qualityValueBadge.textContent = `${state.quality}%`;
        }
        markResultStale();
    });

    // -------------------------------------------------------------
    // Aspect Ratio & Dimension Calculation
    // -------------------------------------------------------------
    function initDimensionsFromMetadata() {
        if (!state.originalMetadata) return;

        state.percentage = 100;
        percentageInput.value = 100;
        percentageSlider.value = 100;
        setActivePresetButton(100);

        state.targetWidth = state.originalMetadata.width;
        state.targetHeight = state.originalMetadata.height;

        state.isUpdatingDimensions = true;
        targetWidthInput.value = state.targetWidth;
        targetHeightInput.value = state.targetHeight;
        state.isUpdatingDimensions = false;

        updateSummary();
    }

    // Resize Mode toggle
    modePercentageRadio.addEventListener("change", () => {
        if (modePercentageRadio.checked) {
            state.resizeMode = "Percentage";
            percentageSection.classList.remove("d-none");
            customDimensionsSection.classList.add("d-none");
            updateDimensionsFromPercentage(state.percentage);
            markResultStale();
        }
    });

    modeCustomRadio.addEventListener("change", () => {
        if (modeCustomRadio.checked) {
            state.resizeMode = "CustomDimensions";
            percentageSection.classList.add("d-none");
            customDimensionsSection.classList.remove("d-none");
            updateDimensionsFromCustomWidth(parseInt(targetWidthInput.value, 10) || state.targetWidth);
            markResultStale();
        }
    });

    // Percentage mode input & slider
    percentageInput.addEventListener("input", (e) => {
        let val = parseInt(e.target.value, 10);
        if (isNaN(val)) return;
        val = Math.max(1, Math.min(500, val));
        percentageSlider.value = val;
        setActivePresetButton(val);
        updateDimensionsFromPercentage(val);
        markResultStale();
    });

    percentageSlider.addEventListener("input", (e) => {
        const val = parseInt(e.target.value, 10);
        percentageInput.value = val;
        setActivePresetButton(val);
        updateDimensionsFromPercentage(val);
        markResultStale();
    });

    presetButtons.forEach(btn => {
        btn.addEventListener("click", () => {
            const percent = parseInt(btn.getAttribute("data-percent"), 10);
            percentageInput.value = percent;
            percentageSlider.value = percent;
            setActivePresetButton(percent);
            updateDimensionsFromPercentage(percent);
            markResultStale();
        });
    });

    function setActivePresetButton(percent) {
        presetButtons.forEach(btn => {
            if (parseInt(btn.getAttribute("data-percent"), 10) === percent) {
                btn.classList.add("active");
            } else {
                btn.classList.remove("active");
            }
        });
    }

    function updateDimensionsFromPercentage(pct) {
        if (!state.originalMetadata) return;
        state.percentage = pct;

        const w = Math.max(1, Math.round(state.originalMetadata.width * pct / 100));
        const h = Math.max(1, Math.round(state.originalMetadata.height * pct / 100));

        state.targetWidth = w;
        state.targetHeight = h;

        state.isUpdatingDimensions = true;
        targetWidthInput.value = w;
        targetHeightInput.value = h;
        state.isUpdatingDimensions = false;

        updateSummary();
    }

    // Custom dimensions inputs
    targetWidthInput.addEventListener("input", (e) => {
        if (state.isUpdatingDimensions) return;
        const width = parseInt(e.target.value, 10);
        updateDimensionsFromCustomWidth(width);
        markResultStale();
    });

    targetHeightInput.addEventListener("input", (e) => {
        if (state.isUpdatingDimensions) return;
        const height = parseInt(e.target.value, 10);
        updateDimensionsFromCustomHeight(height);
        markResultStale();
    });

    function updateDimensionsFromCustomWidth(width) {
        if (!state.originalMetadata || isNaN(width) || width <= 0) {
            state.targetWidth = null;
            updateSummary();
            return;
        }

        state.targetWidth = width;

        if (state.preserveAspectRatio) {
            state.isUpdatingDimensions = true;
            const computedHeight = Math.max(1, Math.round(width * state.originalMetadata.height / state.originalMetadata.width));
            state.targetHeight = computedHeight;
            targetHeightInput.value = computedHeight;
            state.isUpdatingDimensions = false;
        } else {
            state.targetHeight = parseInt(targetHeightInput.value, 10) || state.originalMetadata.height;
        }

        updateSummary();
    }

    function updateDimensionsFromCustomHeight(height) {
        if (!state.originalMetadata || isNaN(height) || height <= 0) {
            state.targetHeight = null;
            updateSummary();
            return;
        }

        state.targetHeight = height;

        if (state.preserveAspectRatio) {
            state.isUpdatingDimensions = true;
            const computedWidth = Math.max(1, Math.round(height * state.originalMetadata.width / state.originalMetadata.height));
            state.targetWidth = computedWidth;
            targetWidthInput.value = computedWidth;
            state.isUpdatingDimensions = false;
        } else {
            state.targetWidth = parseInt(targetWidthInput.value, 10) || state.originalMetadata.width;
        }

        updateSummary();
    }

    // Maintain Aspect Ratio toggle
    aspectRatioLockToggle.addEventListener("change", (e) => {
        state.preserveAspectRatio = e.target.checked;

        if (state.preserveAspectRatio) {
            controlsLockBadge.classList.remove("d-none");
            controlsLockBadge.className = "badge bg-success-subtle text-success-emphasis border border-success-subtle";
            controlsLockBadge.textContent = "Ratio Locked";

            // Re-synchronize height based on current width
            const currentWidth = parseInt(targetWidthInput.value, 10) || state.targetWidth;
            if (currentWidth && currentWidth > 0) {
                updateDimensionsFromCustomWidth(currentWidth);
            }
        } else {
            controlsLockBadge.classList.remove("d-none");
            controlsLockBadge.className = "badge bg-warning-subtle text-warning-emphasis border border-warning-subtle";
            controlsLockBadge.textContent = "Ratio Unlocked";
            updateSummary();
        }

        markResultStale();
    });

    // -------------------------------------------------------------
    // Update Target Summary Box
    // -------------------------------------------------------------
    function updateSummary() {
        if (!state.originalMetadata || !state.targetWidth || !state.targetHeight) {
            summaryDimensions.textContent = "—";
            summaryAspectRatio.textContent = "—";
            summaryFormat.textContent = "—";
            return;
        }

        summaryDimensions.textContent = `${state.targetWidth} × ${state.targetHeight} px`;

        if (state.preserveAspectRatio) {
            summaryAspectRatio.textContent = state.originalMetadata.aspectRatioFormatted || calculateReducedRatio(state.targetWidth, state.targetHeight);
            summaryAspectRatio.className = "badge bg-info-subtle text-info-emphasis border border-info-subtle";
        } else {
            summaryAspectRatio.textContent = calculateReducedRatio(state.targetWidth, state.targetHeight);
            summaryAspectRatio.className = "badge bg-warning-subtle text-warning-emphasis border border-warning-subtle";
        }

        summaryFormat.textContent = state.targetFormat.toUpperCase();
    }

    // -------------------------------------------------------------
    // Resize Action (Executes POST /api/image/resize)
    // -------------------------------------------------------------
    resizeBtn.addEventListener("click", () => {
        submitResize();
    });

    async function submitResize() {
        if (state.isProcessing || !state.selectedFile || !state.inspectionSuccessful || !state.originalMetadata) {
            return;
        }

        clearAlert();

        // Validate target dimensions
        if (!state.targetWidth || state.targetWidth <= 0 || !state.targetHeight || state.targetHeight <= 0) {
            showAlert("Please specify valid positive integer dimensions for resizing.");
            return;
        }

        if (state.targetWidth > MAX_DIMENSION || state.targetHeight > MAX_DIMENSION) {
            showAlert(`Requested dimensions (${state.targetWidth} × ${state.targetHeight}) exceed the maximum allowable limit of ${MAX_DIMENSION} × ${MAX_DIMENSION} pixels.`);
            return;
        }

        // Abort previous resize request if still running
        if (state.resizeAbortController) {
            state.resizeAbortController.abort();
            state.resizeAbortController = null;
        }

        const controller = new AbortController();
        state.resizeAbortController = controller;

        state.isProcessing = true;
        resizeBtn.disabled = true;
        const originalBtnText = resizeBtn.textContent;
        resizeBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span> Processing image...';

        const formData = new FormData();
        formData.append("file", state.selectedFile);
        formData.append("mode", state.resizeMode);
        if (state.resizeMode === "Percentage") {
            formData.append("percentage", state.percentage);
        }
        formData.append("targetWidth", state.targetWidth);
        formData.append("targetHeight", state.targetHeight);
        formData.append("preserveAspectRatio", state.preserveAspectRatio);
        formData.append("quality", state.targetFormat === "png" ? 100 : state.quality);
        formData.append("targetFormat", state.targetFormat);

        try {
            const response = await fetch("/api/image/resize", {
                method: "POST",
                body: formData,
                signal: controller.signal
            });

            let data;
            try {
                data = await response.json();
            } catch {
                data = null;
            }

            if (!response.ok || !data || !data.success) {
                let errorMsg = data?.errorMessage;
                if (!errorMsg) {
                    if (response.status === 429) {
                        const retryAfter = response.headers.get("Retry-After");
                        errorMsg = retryAfter 
                            ? `You're processing images too quickly. Please wait ${retryAfter} seconds and try again.`
                            : "You're processing images too quickly. Please wait a moment and try again.";
                    } else if (response.status === 413) {
                        errorMsg = "The image is too large to process.";
                    } else if (response.status === 500) {
                        errorMsg = "The server could not process the image. Please try again.";
                    } else {
                        errorMsg = "Failed to process image. Please verify resize parameters.";
                    }
                }
                showAlert(errorMsg);
                return;
            }

            handleResizeResponse(data);
        } catch (error) {
            if (error.name === "AbortError") {
                // Resize was aborted by user or new action
                return;
            }
            showAlert("Could not connect to the image processor. Please check your connection.");
        } finally {
            if (state.resizeAbortController === controller) {
                state.resizeAbortController = null;
                state.isProcessing = false;
                resizeBtn.disabled = false;
                resizeBtn.textContent = originalBtnText;
            }
        }
    }

    // -------------------------------------------------------------
    // Handle Resize Result & Preview
    // -------------------------------------------------------------
    function handleResizeResponse(data) {
        if (!data.processedBytes) {
            showAlert("The server returned an empty image payload.");
            return;
        }

        // Convert Base64 into Blob
        const blob = base64ToBlob(data.processedBytes, data.mimeType || "image/png");

        // Revoke previous processed URL if any
        if (state.processedPreviewUrl) {
            URL.revokeObjectURL(state.processedPreviewUrl);
            state.processedPreviewUrl = null;
        }

        state.processedPreviewUrl = URL.createObjectURL(blob);
        state.outputMetadata = data.outputMetadata;
        state.suggestedFileName = data.suggestedFileName || "resized_image";
        state.outputMimeType = data.mimeType;
        state.resultIsCurrent = true;

        // Hide stale notices
        staleNotice.classList.add("d-none");
        staleBadge.classList.add("d-none");

        // Populate Images
        resultOriginalImage.src = state.originalPreviewUrl;
        resultProcessedImage.src = state.processedPreviewUrl;

        resultCardTitle.textContent = "Resize Complete";
        resultOutputColumnTitle.textContent = "Resized";
        downloadBtn.textContent = "Download Resized Image";

        // Populate Original Metadata Column
        const origMeta = data.originalMetadata || state.originalMetadata;
        resultOriginalDimensionsBadge.textContent = `${origMeta.width} × ${origMeta.height}`;
        resultOriginalFormat.textContent = origMeta.format;
        resultOriginalRatio.textContent = origMeta.aspectRatioFormatted || "—";
        resultOriginalSize.textContent = formatBytes(origMeta.fileSizeBytes || state.selectedFile.size);

        // Populate Resized Metadata Column
        const outMeta = data.outputMetadata;
        resultOutputDimensionsBadge.textContent = `${outMeta.width} × ${outMeta.height}`;
        resultOutputFormat.textContent = outMeta.format;
        resultOutputRatio.textContent = outMeta.aspectRatioFormatted || "—";
        resultOutputSize.textContent = formatBytes(outMeta.fileSizeBytes);

        // Size Comparison
        const origBytes = origMeta.fileSizeBytes || state.selectedFile.size;
        const procBytes = outMeta.fileSizeBytes;
        sizeComparisonText.textContent = `${formatBytes(origBytes)} → ${formatBytes(procBytes)}`;

        const diff = procBytes - origBytes;
        const pctChange = Math.round((diff / origBytes) * 100);

        if (pctChange < 0) {
            sizeDeltaBadge.className = "badge bg-success fs-6 py-2 px-3";
            sizeDeltaBadge.textContent = `File size reduced by ${Math.abs(pctChange)}%`;
        } else if (pctChange > 0) {
            sizeDeltaBadge.className = "badge bg-secondary fs-6 py-2 px-3";
            sizeDeltaBadge.textContent = `File size increased by ${pctChange}%`;
        } else {
            sizeDeltaBadge.className = "badge bg-info fs-6 py-2 px-3";
            sizeDeltaBadge.textContent = "File size unchanged";
        }

        // Show result section and scroll to it
        resultSection.classList.remove("d-none");
        resultSection.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    // -------------------------------------------------------------
    // Compression Mode, Configuration & Action
    // -------------------------------------------------------------
    function setupCompressionControls(metadata, file) {
        if (!metadata) return;

        compressOrigDimensions.textContent = `${metadata.width} × ${metadata.height} px`;
        compressOrigFormat.textContent = metadata.format;
        compressOrigSize.textContent = formatBytes(metadata.fileSizeBytes || file?.size);

        compressTargetDimensions.textContent = `${metadata.width} × ${metadata.height} px`;
        compressTargetFormat.textContent = metadata.format;

        const fmt = (metadata.format || "").toUpperCase();

        if (fmt === "JPEG" || fmt === "JPG" || fmt === "WEBP") {
            compressQualitySection.classList.remove("d-none");
            compressPngSection.classList.add("d-none");
            compressUnsupportedSection.classList.add("d-none");
            compressReadyBanner.classList.remove("d-none");
            compressBtn.disabled = false;

            state.compressQuality = 75;
            compressQualitySlider.value = 75;
            compressQualityBadge.textContent = "75%";
            setActiveCompressPresetButton(75);
        } else if (fmt === "PNG") {
            compressQualitySection.classList.add("d-none");
            compressPngSection.classList.remove("d-none");
            compressUnsupportedSection.classList.add("d-none");
            compressReadyBanner.classList.remove("d-none");
            compressBtn.disabled = false;

            state.pngCompressionLevel = 9;
            setActiveCompressPngButton(9);
        } else {
            // GIF / BMP or unsupported
            compressQualitySection.classList.add("d-none");
            compressPngSection.classList.add("d-none");
            compressUnsupportedSection.classList.remove("d-none");
            compressReadyBanner.classList.add("d-none");
            compressBtn.disabled = true;
        }
    }

    function setToolMode(mode) {
        if (state.currentToolMode === mode) return;

        state.currentToolMode = mode;

        if (mode === "Resize") {
            tabResizeMode.classList.remove("btn-outline-primary");
            tabResizeMode.classList.add("btn-primary");
            tabCompressMode.classList.remove("btn-primary");
            tabCompressMode.classList.add("btn-outline-primary");

            controlsCard.classList.remove("d-none");
            compressControlsCard.classList.add("d-none");

            if (resultSection && !resultSection.classList.contains("d-none")) {
                markResultStale();
            }
        } else if (mode === "Compress") {
            tabCompressMode.classList.remove("btn-outline-primary");
            tabCompressMode.classList.add("btn-primary");
            tabResizeMode.classList.remove("btn-primary");
            tabResizeMode.classList.add("btn-outline-primary");

            controlsCard.classList.add("d-none");
            compressControlsCard.classList.remove("d-none");

            if (state.originalMetadata) {
                setupCompressionControls(state.originalMetadata, state.selectedFile);
            }

            if (resultSection && !resultSection.classList.contains("d-none")) {
                markResultStale();
            }
        }
    }

    tabResizeMode.addEventListener("click", () => setToolMode("Resize"));
    tabCompressMode.addEventListener("click", () => setToolMode("Compress"));

    compressQualitySlider.addEventListener("input", (e) => {
        state.compressQuality = parseInt(e.target.value, 10);
        compressQualityBadge.textContent = `${state.compressQuality}%`;
        setActiveCompressPresetButton(state.compressQuality);
        markResultStale();
    });

    compressPresetBtns.forEach(btn => {
        btn.addEventListener("click", () => {
            const q = parseInt(btn.getAttribute("data-quality"), 10);
            state.compressQuality = q;
            compressQualitySlider.value = q;
            compressQualityBadge.textContent = `${q}%`;
            setActiveCompressPresetButton(q);
            markResultStale();
        });
    });

    function setActiveCompressPresetButton(quality) {
        compressPresetBtns.forEach(btn => {
            if (parseInt(btn.getAttribute("data-quality"), 10) === quality) {
                btn.classList.add("active");
            } else {
                btn.classList.remove("active");
            }
        });
    }

    compressPngBtns.forEach(btn => {
        btn.addEventListener("click", () => {
            const level = parseInt(btn.getAttribute("data-level"), 10);
            state.pngCompressionLevel = level;
            setActiveCompressPngButton(level);
            markResultStale();
        });
    });

    function setActiveCompressPngButton(level) {
        compressPngBtns.forEach(btn => {
            if (parseInt(btn.getAttribute("data-level"), 10) === level) {
                btn.classList.add("active");
            } else {
                btn.classList.remove("active");
            }
        });
    }

    compressBtn.addEventListener("click", () => {
        submitCompress();
    });

    compressResetBtn.addEventListener("click", () => {
        resetState();
    });

    async function submitCompress() {
        if (state.isProcessing || !state.selectedFile || !state.inspectionSuccessful || !state.originalMetadata) {
            return;
        }

        const fmt = (state.originalMetadata.format || "").toUpperCase();
        if (fmt !== "JPEG" && fmt !== "JPG" && fmt !== "PNG" && fmt !== "WEBP") {
            showAlert("Compression is not supported for this format without conversion.");
            return;
        }

        clearAlert();

        if (state.compressAbortController) {
            state.compressAbortController.abort();
            state.compressAbortController = null;
        }

        const controller = new AbortController();
        state.compressAbortController = controller;

        state.isProcessing = true;
        compressBtn.disabled = true;
        const originalBtnText = compressBtn.textContent;
        compressBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span> Processing image...';

        const formData = new FormData();
        formData.append("file", state.selectedFile);
        formData.append("quality", state.compressQuality);
        formData.append("pngCompressionLevel", state.pngCompressionLevel);

        try {
            const response = await fetch("/api/image/compress", {
                method: "POST",
                body: formData,
                signal: controller.signal
            });

            let data;
            try {
                data = await response.json();
            } catch {
                data = null;
            }

            if (!response.ok || !data || !data.success) {
                let errorMsg = data?.errorMessage;
                if (!errorMsg) {
                    if (response.status === 429) {
                        const retryAfter = response.headers.get("Retry-After");
                        errorMsg = retryAfter 
                            ? `You're processing images too quickly. Please wait ${retryAfter} seconds and try again.`
                            : "You're processing images too quickly. Please wait a moment and try again.";
                    } else if (response.status === 413) {
                        errorMsg = "The image is too large to process.";
                    } else if (response.status === 500) {
                        errorMsg = "The server could not process the image. Please try again.";
                    } else {
                        errorMsg = "Failed to compress image. Please verify parameters.";
                    }
                }
                showAlert(errorMsg);
                return;
            }

            handleCompressResponse(data);
        } catch (error) {
            if (error.name === "AbortError") {
                return;
            }
            showAlert("Could not connect to the compression service. Please check your connection.");
        } finally {
            if (state.compressAbortController === controller) {
                state.compressAbortController = null;
                state.isProcessing = false;
                compressBtn.disabled = false;
                compressBtn.textContent = originalBtnText;
            }
        }
    }

    function handleCompressResponse(data) {
        if (!data.processedBytes) {
            showAlert("The server returned an empty image payload.");
            return;
        }

        const blob = base64ToBlob(data.processedBytes, data.mimeType || "image/jpeg");

        if (state.processedPreviewUrl) {
            URL.revokeObjectURL(state.processedPreviewUrl);
            state.processedPreviewUrl = null;
        }

        state.processedPreviewUrl = URL.createObjectURL(blob);
        state.outputMetadata = data.outputMetadata;
        state.suggestedFileName = data.suggestedFileName || "compressed_image";
        state.outputMimeType = data.mimeType;
        state.resultIsCurrent = true;

        staleNotice.classList.add("d-none");
        staleBadge.classList.add("d-none");

        resultOriginalImage.src = state.originalPreviewUrl;
        resultProcessedImage.src = state.processedPreviewUrl;

        resultCardTitle.textContent = "Compression Complete";
        resultOutputColumnTitle.textContent = "Compressed";
        downloadBtn.textContent = "Download Compressed Image";

        const origMeta = data.originalMetadata || state.originalMetadata;
        resultOriginalDimensionsBadge.textContent = `${origMeta.width} × ${origMeta.height}`;
        resultOriginalFormat.textContent = origMeta.format;
        resultOriginalRatio.textContent = origMeta.aspectRatioFormatted || "—";
        resultOriginalSize.textContent = formatBytes(data.originalFileSizeBytes || origMeta.fileSizeBytes || state.selectedFile.size);

        const outMeta = data.outputMetadata;
        resultOutputDimensionsBadge.textContent = `${outMeta.width} × ${outMeta.height}`;
        resultOutputFormat.textContent = outMeta.format;
        resultOutputRatio.textContent = outMeta.aspectRatioFormatted || "—";
        resultOutputSize.textContent = formatBytes(data.compressedFileSizeBytes || outMeta.fileSizeBytes);

        const origBytes = data.originalFileSizeBytes || origMeta.fileSizeBytes || state.selectedFile.size;
        const compBytes = data.compressedFileSizeBytes || outMeta.fileSizeBytes;
        sizeComparisonText.textContent = `${formatBytes(origBytes)} → ${formatBytes(compBytes)}`;

        if (data.statusMessage) {
            sizeDeltaBadge.textContent = data.statusMessage;
            if (data.isAlreadyOptimized || data.savedBytes === 0 || compBytes >= origBytes) {
                sizeDeltaBadge.className = "badge bg-info-subtle text-info-emphasis border border-info-subtle fs-6 py-2 px-3 rounded-pill font-monospace";
            } else {
                sizeDeltaBadge.className = "badge bg-success fs-6 py-2 px-3 rounded-pill font-monospace";
            }
        } else if (data.isAlreadyOptimized || compBytes >= origBytes || data.savedBytes === 0) {
            sizeDeltaBadge.className = "badge bg-info-subtle text-info-emphasis border border-info-subtle fs-6 py-2 px-3 rounded-pill font-monospace";
            sizeDeltaBadge.textContent = "PNG was already optimized — no smaller lossless representation found";
        } else {
            sizeDeltaBadge.className = "badge bg-success fs-6 py-2 px-3 rounded-pill font-monospace";
            sizeDeltaBadge.textContent = `Compressed successfully — ${data.reductionPercentage}% smaller (Saved ${formatBytes(data.savedBytes)})`;
        }

        resultSection.classList.remove("d-none");
        resultSection.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    // -------------------------------------------------------------
    // Download Action
    // -------------------------------------------------------------
    downloadBtn.addEventListener("click", () => {
        if (!state.processedPreviewUrl) {
            showAlert("No processed image available for download.");
            return;
        }

        const anchor = document.createElement("a");
        anchor.href = state.processedPreviewUrl;
        anchor.download = state.suggestedFileName || "resized_image";
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);
    });

    // -------------------------------------------------------------
    // Reset Action & Upload Another
    // -------------------------------------------------------------
    resetBtn.addEventListener("click", () => {
        resetState();
    });

    uploadAnotherBtn.addEventListener("click", () => {
        resetState();
        imageFileInput.click();
    });

    function resetState() {
        // Abort in-flight requests
        if (state.inspectAbortController) {
            state.inspectAbortController.abort();
            state.inspectAbortController = null;
        }
        if (state.resizeAbortController) {
            state.resizeAbortController.abort();
            state.resizeAbortController = null;
        }

        // Revoke object URLs to prevent browser memory leaks
        if (state.originalPreviewUrl) {
            URL.revokeObjectURL(state.originalPreviewUrl);
            state.originalPreviewUrl = null;
        }
        if (state.processedPreviewUrl) {
            URL.revokeObjectURL(state.processedPreviewUrl);
            state.processedPreviewUrl = null;
        }

        state.selectedFile = null;
        state.originalMetadata = null;
        state.outputMetadata = null;
        state.suggestedFileName = null;
        state.outputMimeType = null;
        state.inspectionSuccessful = false;
        state.resultIsCurrent = false;
        state.resizeMode = "Percentage";
        state.percentage = 100;
        state.targetWidth = null;
        state.targetHeight = null;
        state.preserveAspectRatio = true;
        state.quality = 85;
        state.targetFormat = "jpeg";
        state.isInspecting = false;
        state.isProcessing = false;
        state.isUpdatingDimensions = false;

        // Reset UI inputs
        imageFileInput.value = "";
        originalImagePreview.src = "";
        resultOriginalImage.src = "";
        resultProcessedImage.src = "";

        previewCard.classList.add("d-none");
        metadataCard.classList.add("d-none");
        inspectingIndicator.classList.add("d-none");
        readyBanner.classList.add("d-none");
        resultSection.classList.add("d-none");
        staleNotice.classList.add("d-none");
        staleBadge.classList.add("d-none");
        clearAlert();

        modePercentageRadio.checked = true;
        percentageSection.classList.remove("d-none");
        customDimensionsSection.classList.add("d-none");

        percentageInput.value = 100;
        percentageSlider.value = 100;
        setActivePresetButton(100);

        aspectRatioLockToggle.checked = true;
        controlsLockBadge.className = "badge bg-success-subtle text-success-emphasis border border-success-subtle";
        controlsLockBadge.textContent = "Ratio Locked";

        targetWidthInput.value = "";
        targetHeightInput.value = "";

        outputFormatSelect.value = "jpeg";
        formatNotice.textContent = "";

        qualitySlider.value = 85;
        qualityValueBadge.textContent = "85%";
        qualitySection.classList.remove("quality-disabled");
        qualitySlider.disabled = false;

        summaryDimensions.textContent = "—";
        summaryAspectRatio.textContent = "—";
        summaryFormat.textContent = "—";

        resizeBtn.disabled = true;

        // Reset compression state and UI
        if (state.compressAbortController) {
            state.compressAbortController.abort();
            state.compressAbortController = null;
        }

        state.currentToolMode = "Resize";
        tabResizeMode.classList.remove("btn-outline-primary");
        tabResizeMode.classList.add("btn-primary");
        tabCompressMode.classList.remove("btn-primary");
        tabCompressMode.classList.add("btn-outline-primary");

        if (initialStatePrompt) initialStatePrompt.classList.remove("d-none");
        if (toolModeCard) toolModeCard.classList.add("d-none");
        controlsCard.classList.add("d-none");
        compressControlsCard.classList.add("d-none");

        state.compressQuality = 75;
        state.pngCompressionLevel = 9;
        compressQualitySlider.value = 75;
        compressQualityBadge.textContent = "75%";
        setActiveCompressPresetButton(75);
        setActiveCompressPngButton(9);
        compressBtn.disabled = true;

        compressOrigDimensions.textContent = "—";
        compressOrigFormat.textContent = "—";
        compressOrigSize.textContent = "—";
        compressTargetDimensions.textContent = "—";
        compressTargetFormat.textContent = "—";
        compressQualitySection.classList.add("d-none");
        compressPngSection.classList.add("d-none");
        compressUnsupportedSection.classList.add("d-none");
        compressReadyBanner.classList.add("d-none");
    }
});

