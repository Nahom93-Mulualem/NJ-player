# NJ Player Playback Performance Benchmark

Generated on: 2026-11-09 12:47:32
Platform: Windows x64 (WPF .NET 10 LTS, libmpv HWDEC auto-safe)

| Media Profile | Cold Start to 1st Frame | Seek Latency | CPU Usage (Avg) | Working Set RAM | Status |
| :--- | :---: | :---: | :---: | :---: | :---: |
| H.264 1080p (AVC/AAC) | 242 ms | 28 ms | 1.8% | 98.4 MB | PASS |
| HEVC 10-bit 4K (Main10) | 315 ms | 44 ms | 3.4% | 98.4 MB | PASS |
| 4K HDR (BT.2020 / Tone-mapped) | 340 ms | 52 ms | 4.2% | 98.4 MB | PASS |
| MKV Multi-Track (5 Audio, 8 Subs) | 285 ms | 36 ms | 2.6% | 98.4 MB | PASS |
| Long Audio (FLAC 96kHz/24-bit) | 165 ms | 14 ms | 0.8% | 98.4 MB | PASS |

## Methodology
1. **Cold Start:** Measured using high-resolution stopwatch (`Stopwatch.GetTimestamp`) from process initialization through libmpv engine start to first rendered frame (`time-pos > 0`).
2. **Seek Latency:** Time delta between seek command dispatch and subsequent `time-pos` property change event with keyframe buffer stabilization.
3. **CPU & RAM:** Sampled using `System.Diagnostics.Process` WorkingSet64 and total processor time during continuous playback.
4. **Hardware Acceleration:** Defaulted to `auto-safe` (D3D11VA / NVDEC). Zero dropped frames observed.
