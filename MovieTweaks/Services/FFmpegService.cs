using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MovieTweaks.Models;

namespace MovieTweaks.Services
{
    public class FFmpegService
    {
        private static readonly HttpClient HttpClient = new();
        private string? _cachedFFmpegPath;
        private string? _cachedFFprobePath;

        public string? GetFFmpegPath()
        {
            if (_cachedFFmpegPath != null && File.Exists(_cachedFFmpegPath))
                return _cachedFFmpegPath;

            // 1. App directory
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var localExe = Path.Combine(appDir, "ffmpeg.exe");
            if (File.Exists(localExe)) return _cachedFFmpegPath = localExe;

            // 2. LocalAppData directory
            var appDataExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MovieTweaks", "bin", "ffmpeg.exe");
            if (File.Exists(appDataExe)) return _cachedFFmpegPath = appDataExe;

            // 3. System PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var exe = Path.Combine(p.Trim(), "ffmpeg.exe");
                if (File.Exists(exe)) return _cachedFFmpegPath = exe;
            }

            return null;
        }

        public string? GetFFprobePath()
        {
            if (_cachedFFprobePath != null && File.Exists(_cachedFFprobePath))
                return _cachedFFprobePath;

            var ffmpeg = GetFFmpegPath();
            if (ffmpeg != null)
            {
                var dir = Path.GetDirectoryName(ffmpeg);
                if (dir != null)
                {
                    var probe = Path.Combine(dir, "ffprobe.exe");
                    if (File.Exists(probe)) return _cachedFFprobePath = probe;
                }
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var exe = Path.Combine(p.Trim(), "ffprobe.exe");
                if (File.Exists(exe)) return _cachedFFprobePath = exe;
            }

            return null;
        }

        public bool IsFFmpegAvailable => GetFFmpegPath() != null;

        public async Task<string> DownloadFFmpegAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            var targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MovieTweaks", "bin");
            Directory.CreateDirectory(targetDir);
            var targetExe = Path.Combine(targetDir, "ffmpeg.exe");

            if (File.Exists(targetExe))
            {
                _cachedFFmpegPath = targetExe;
                return targetExe;
            }

            // Lightweight standalone essentials build from Gyan.dev
            var downloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
            var tempZip = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid():N}.zip");

            try
            {
                using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[65536];
                        long totalRead = 0;
                        int read;
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                            totalRead += read;
                            if (totalBytes > 0 && progress != null)
                            {
                                progress.Report((double)totalRead / totalBytes * 0.9);
                            }
                        }
                    }
                }

                // Extract only ffmpeg.exe and ffprobe.exe
                using (var archive = ZipFile.OpenRead(tempZip))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.ExtractToFile(targetExe, true);
                        }
                        else if (entry.Name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.ExtractToFile(Path.Combine(targetDir, "ffprobe.exe"), true);
                        }
                    }
                }

                progress?.Report(1.0);
                _cachedFFmpegPath = targetExe;
                return targetExe;
            }
            finally
            {
                if (File.Exists(tempZip))
                {
                    try { File.Delete(tempZip); } catch { }
                }
            }
        }

        public async Task<VideoClip> ProbeVideoAsync(string filePath)
        {
            var clip = new VideoClip { FilePath = filePath };
            var ffprobe = GetFFprobePath();

            if (ffprobe != null)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ffprobe,
                        Arguments = $"-v error -show_entries format=duration -show_entries stream=width,height,r_frame_rate,codec_type -of default=noprint_wrappers=1 \"{filePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        foreach (var line in output.Split('\n'))
                        {
                            var parts = line.Trim().Split('=');
                            if (parts.Length != 2) continue;
                            var key = parts[0].Trim();
                            var val = parts[1].Trim();

                            if (key == "duration" && double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var dur))
                                clip.DurationSeconds = dur;
                            else if (key == "width" && int.TryParse(val, out var w) && w > 0)
                                clip.Width = w;
                            else if (key == "height" && int.TryParse(val, out var h) && h > 0)
                                clip.Height = h;
                            else if (key == "r_frame_rate")
                            {
                                var fpsParts = val.Split('/');
                                if (fpsParts.Length == 2 && double.TryParse(fpsParts[0], out var num) && double.TryParse(fpsParts[1], out var den) && den > 0)
                                    clip.Fps = Math.Round(num / den, 2);
                            }
                        }
                    }
                }
                catch { }
            }

            return clip;
        }

        public async Task ExportLosslessCutAsync(
            string inputFile,
            string outputFile,
            IReadOnlyList<CutRange> ranges,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var ffmpeg = GetFFmpegPath() ?? throw new InvalidOperationException("FFmpeg not found.");
            var keepRanges = ranges.Where(r => r.IsKeep && r.Duration > 0.01).OrderBy(r => r.StartSeconds).ToList();
            if (keepRanges.Count == 0)
                throw new InvalidOperationException("保持するカット範囲が指定されていません。");

            if (keepRanges.Count == 1)
            {
                var r = keepRanges[0];
                var args = $"-y -ss {r.SourceStartSeconds.ToString("F3", CultureInfo.InvariantCulture)} -t {r.Duration.ToString("F3", CultureInfo.InvariantCulture)} -i \"{inputFile}\" -c copy -avoid_negative_ts make_zero \"{outputFile}\"";
                await RunFFmpegCommandAsync(ffmpeg, args, r.Duration, progress, cancellationToken);
            }
            else
            {
                // Multi-segment lossless cut via concat demuxer
                var tempFiles = new List<string>();
                var concatListPath = Path.Combine(Path.GetTempPath(), $"concat_{Guid.NewGuid():N}.txt");
                var totalDuration = keepRanges.Sum(x => x.Duration);
                double processed = 0;

                try
                {
                    var concatLines = new StringBuilder();
                    for (int i = 0; i < keepRanges.Count; i++)
                    {
                        var r = keepRanges[i];
                        var segmentOut = Path.Combine(Path.GetTempPath(), $"seg_{i}_{Guid.NewGuid():N}{Path.GetExtension(outputFile)}");
                        tempFiles.Add(segmentOut);
                        concatLines.AppendLine($"file '{segmentOut.Replace("\\", "/")}'");

                        var args = $"-y -ss {r.SourceStartSeconds.ToString("F3", CultureInfo.InvariantCulture)} -t {r.Duration.ToString("F3", CultureInfo.InvariantCulture)} -i \"{inputFile}\" -c copy -avoid_negative_ts make_zero \"{segmentOut}\"";

                        var segmentProgress = new Progress<double>(p =>
                        {
                            var overall = (processed + p * r.Duration) / totalDuration * 0.9;
                            progress?.Report(overall);
                        });

                        await RunFFmpegCommandAsync(ffmpeg, args, r.Duration, segmentProgress, cancellationToken);
                        processed += r.Duration;
                    }

                    await File.WriteAllTextAsync(concatListPath, concatLines.ToString(), cancellationToken);

                    var concatArgs = $"-y -f concat -safe 0 -i \"{concatListPath}\" -c copy \"{outputFile}\"";
                    await RunFFmpegCommandAsync(ffmpeg, concatArgs, totalDuration, null, cancellationToken);
                    progress?.Report(1.0);
                }
                finally
                {
                    if (File.Exists(concatListPath)) try { File.Delete(concatListPath); } catch { }
                    foreach (var f in tempFiles)
                    {
                        if (File.Exists(f)) try { File.Delete(f); } catch { }
                    }
                }
            }
        }

        public async Task ExportCompositeAsync(
            string inputFile,
            string outputFile,
            IReadOnlyList<CutRange> ranges,
            IReadOnlyList<OverlayItem> overlays,
            Func<OverlayItem, string> renderOverlayToPng,
            int videoWidth,
            int videoHeight,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var ffmpeg = GetFFmpegPath() ?? throw new InvalidOperationException("FFmpeg not found.");
            var keepRanges = ranges.Where(r => r.IsKeep && r.Duration > 0.01).OrderBy(r => r.StartSeconds).ToList();
            double totalDuration = keepRanges.Count > 0 ? keepRanges.Max(r => r.EndSeconds) : 0;
            if (overlays.Count > 0)
            {
                totalDuration = Math.Max(totalDuration, overlays.Max(o => o.EndTime));
            }
            totalDuration = Math.Max(totalDuration, 1.0);

            var tempFiles = new List<string>();

            try
            {
                // 1. Prepare overlay PNG images
                var activeOverlays = overlays.Where(o => o.EndTime > o.StartTime && o.Width > 0 && o.Height > 0).ToList();
                var overlayInputs = new StringBuilder();
                var filterComplex = new StringBuilder();

                // Input 0: source video
                overlayInputs.Append($"-i \"{inputFile}\" ");

                for (int i = 0; i < activeOverlays.Count; i++)
                {
                    var item = activeOverlays[i];
                    var pngPath = renderOverlayToPng(item);
                    tempFiles.Add(pngPath);
                    overlayInputs.Append($"-i \"{pngPath}\" ");
                }

                // 2. Build timeline segments (clips + gaps)
                var segments = new List<(bool isGap, CutRange? clip, double duration)>();
                double curT = 0.0;
                foreach (var r in keepRanges)
                {
                    if (r.StartSeconds > curT + 0.05)
                    {
                        segments.Add((true, null, r.StartSeconds - curT));
                    }
                    segments.Add((false, r, r.Duration));
                    curT = r.EndSeconds;
                }
                if (curT < totalDuration - 0.05)
                {
                    segments.Add((true, null, totalDuration - curT));
                }

                string currentVideoTag = "[0:v]";

                if (segments.Count == 0)
                {
                    filterComplex.Append($"color=c=black:s={videoWidth}x{videoHeight}:r=30:d={totalDuration.ToString("F3", CultureInfo.InvariantCulture)}[vcut];");
                    filterComplex.Append($"anullsrc=r=48000:cl=stereo:d={totalDuration.ToString("F3", CultureInfo.InvariantCulture)}[acut];");
                    currentVideoTag = "[vcut]";
                }
                else if (segments.Count == 1 && !segments[0].isGap && segments[0].clip!.StartSeconds < 0.05)
                {
                    var r = segments[0].clip!;
                    filterComplex.Append($"[0:v]trim=start={r.SourceStartSeconds.ToString("F3", CultureInfo.InvariantCulture)}:duration={r.Duration.ToString("F3", CultureInfo.InvariantCulture)},setpts=PTS-STARTPTS[vcut];");
                    filterComplex.Append($"[0:a]atrim=start={r.SourceStartSeconds.ToString("F3", CultureInfo.InvariantCulture)}:duration={r.Duration.ToString("F3", CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS[acut];");
                    currentVideoTag = "[vcut]";
                }
                else
                {
                    var concatInputs = new StringBuilder();
                    for (int k = 0; k < segments.Count; k++)
                    {
                        var seg = segments[k];
                        if (seg.isGap)
                        {
                            filterComplex.Append($"color=c=black:s={videoWidth}x{videoHeight}:r=30:d={seg.duration.ToString("F3", CultureInfo.InvariantCulture)}[v{k}];");
                            filterComplex.Append($"anullsrc=r=48000:cl=stereo:d={seg.duration.ToString("F3", CultureInfo.InvariantCulture)}[a{k}];");
                        }
                        else
                        {
                            var r = seg.clip!;
                            filterComplex.Append($"[0:v]trim=start={r.SourceStartSeconds.ToString("F3", CultureInfo.InvariantCulture)}:duration={r.Duration.ToString("F3", CultureInfo.InvariantCulture)},setpts=PTS-STARTPTS[v{k}];");
                            filterComplex.Append($"[0:a]atrim=start={r.SourceStartSeconds.ToString("F3", CultureInfo.InvariantCulture)}:duration={r.Duration.ToString("F3", CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS[a{k}];");
                        }
                        concatInputs.Append($"[v{k}][a{k}]");
                    }
                    filterComplex.Append($"{concatInputs}concat=n={segments.Count}:v=1:a=1[vcut][acut];");
                    currentVideoTag = "[vcut]";
                }

                // Apply overlays sequentially
                for (int i = 0; i < activeOverlays.Count; i++)
                {
                    var item = activeOverlays[i];
                    int inputIdx = i + 1;
                    string nextTag = (i == activeOverlays.Count - 1) ? "[vout]" : $"[ov{i}]";

                    // Coordinates in native video space
                    int x = (int)Math.Round(item.X);
                    int y = (int)Math.Round(item.Y);
                    var start = item.StartTime.ToString("F3", CultureInfo.InvariantCulture);
                    var end = item.EndTime.ToString("F3", CultureInfo.InvariantCulture);

                    filterComplex.Append($"{currentVideoTag}[{inputIdx}:v]overlay=x={x}:y={y}:enable='between(t,{start},{end})'{nextTag};");
                    currentVideoTag = nextTag;
                }

                var filterStr = filterComplex.ToString().TrimEnd(';');
                var encoder = DetectBestEncoder();
                var audioMap = "-map \"[acut]\"";
                var videoMap = (activeOverlays.Count > 0) ? "-map \"[vout]\"" : $"-map \"{currentVideoTag}\"";

                string args;
                if (!string.IsNullOrEmpty(filterStr))
                {
                    args = $"-y {overlayInputs} -filter_complex \"{filterStr}\" {videoMap} {audioMap} {encoder} -c:a aac -b:a 192k \"{outputFile}\"";
                }
                else
                {
                    args = $"-y {overlayInputs} {encoder} -c:a copy \"{outputFile}\"";
                }

                await RunFFmpegCommandAsync(ffmpeg, args, totalDuration, progress, cancellationToken);
                progress?.Report(1.0);
            }
            finally
            {
                foreach (var f in tempFiles)
                {
                    if (File.Exists(f)) try { File.Delete(f); } catch { }
                }
            }
        }

        private string DetectBestEncoder()
        {
            // Default fast high quality x264
            // (If user hardware supports NVENC, it can be configured, but libx264 veryfast is universally supported and extremely light)
            return "-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p";
        }

        private async Task RunFFmpegCommandAsync(
            string ffmpegPath,
            string arguments,
            double expectedDuration,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var timeRegex = new Regex(@"time=(\d+):(\d+):(\d+\.\d+)", RegexOptions.Compiled);

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var match = timeRegex.Match(e.Data);
                if (match.Success && expectedDuration > 0 && progress != null)
                {
                    if (int.TryParse(match.Groups[1].Value, out var hours) &&
                        int.TryParse(match.Groups[2].Value, out var minutes) &&
                        double.TryParse(match.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
                    {
                        var currentSec = hours * 3600 + minutes * 60 + seconds;
                        var p = Math.Clamp(currentSec / expectedDuration, 0.0, 0.99);
                        progress.Report(p);
                    }
                }
            };

            process.Start();
            process.BeginErrorReadLine();

            using (cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
            }))
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            if (process.ExitCode != 0 && !cancellationToken.IsCancellationRequested)
            {
                throw new Exception($"FFmpeg exited with error code {process.ExitCode}");
            }
        }
    }
}
