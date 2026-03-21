using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using McServerManager.Models;
using McServerManager.Utilities;

namespace McServerManager.Services;

public sealed class AppUpdateService
{
    public const string ManifestUrl = "https://www.maipilot.jp/updates/win-x64/update.json";
    // Temporary release fallback: allow unsigned installer updates.
    // Set to false to enforce Authenticode verification again.
    private static readonly bool SkipAuthenticodeVerification = true;
    private const string TrustedSignerSubject = "CN=MaiPilot";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public AppUpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildManifestUrl());
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
                MaxAge = TimeSpan.Zero
            };
            request.Headers.Pragma.ParseAdd("no-cache");

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var manifestJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<AppUpdateManifest>(manifestJson, JsonOptions);
            if (!TryNormalizeManifest(manifest, out var normalizedManifest, out var manifestError))
            {
                return AppUpdateCheckResult.Failed(manifestError ?? "update.json の形式が不正です。");
            }

            if (!TryParseVersion(GetCurrentAppVersion(), out var currentVersion))
            {
                return AppUpdateCheckResult.Failed("現在のアプリバージョンを解析できませんでした。");
            }

            if (!TryParseVersion(normalizedManifest.Version, out var latestVersion))
            {
                return AppUpdateCheckResult.Failed("update.json の version が不正です。");
            }

            return latestVersion > currentVersion
                ? AppUpdateCheckResult.UpdateAvailable(normalizedManifest)
                : AppUpdateCheckResult.UpToDate();
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? AppUpdateCheckResult.Failed("更新チェックがキャンセルされました。")
                : AppUpdateCheckResult.Failed("更新情報の取得がタイムアウトしました。");
        }
        catch (HttpRequestException ex)
        {
            return AppUpdateCheckResult.Failed($"更新情報の取得に失敗しました: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return AppUpdateCheckResult.Failed($"update.json の解析に失敗しました: {ex.Message}");
        }
        catch (Exception ex)
        {
            return AppUpdateCheckResult.Failed($"更新チェック中にエラーが発生しました: {ex.Message}");
        }
    }

    public async Task<AppUpdateDownloadResult> DownloadAndLaunchInstallerAsync(
        AppUpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeManifest(manifest, out var normalizedManifest, out var manifestError))
        {
            return AppUpdateDownloadResult.DownloadFailed(manifestError ?? "update.json の形式が不正です。");
        }

        var installerPath = GetInstallerTempPath(normalizedManifest.Version);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(installerPath)!);

            using var response = await _httpClient.GetAsync(
                normalizedManifest.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = File.Create(installerPath))
            {
                await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            var actualHash = await ComputeSha256Async(installerPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, normalizedManifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return AppUpdateDownloadResult.HashMismatch(
                    $"SHA256 が一致しません。 expected={normalizedManifest.Sha256}, actual={actualHash}");
            }

            if (!SkipAuthenticodeVerification)
            {
                if (!AuthenticodeVerifier.TryVerify(installerPath, out var signerSubject, out var signError))
                {
                    return AppUpdateDownloadResult.SignatureInvalid(
                        $"Authenticode 検証に失敗しました。{signError}");
                }

                if (!IsTrustedSignerSubject(signerSubject))
                {
                    return AppUpdateDownloadResult.SignatureInvalid(
                        $"署名 Subject が不一致です。 expected={TrustedSignerSubject}, actual={signerSubject ?? "(null)"}");
                }
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            };
            _ = System.Diagnostics.Process.Start(startInfo);

            return AppUpdateDownloadResult.Success(installerPath);
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? AppUpdateDownloadResult.DownloadFailed("更新処理がキャンセルされました。")
                : AppUpdateDownloadResult.DownloadFailed("インストーラーの取得がタイムアウトしました。");
        }
        catch (HttpRequestException ex)
        {
            return AppUpdateDownloadResult.DownloadFailed($"インストーラーの取得に失敗しました: {ex.Message}");
        }
        catch (Exception ex)
        {
            return AppUpdateDownloadResult.DownloadFailed($"更新処理中にエラーが発生しました: {ex.Message}");
        }
    }

    private static string GetCurrentAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Trim();
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static bool TryNormalizeManifest(
        AppUpdateManifest? manifest,
        out AppUpdateManifest normalizedManifest,
        out string? errorMessage)
    {
        normalizedManifest = new AppUpdateManifest();
        errorMessage = null;

        if (manifest is null)
        {
            errorMessage = "update.json が空です。";
            return false;
        }

        var version = manifest.Version?.Trim();
        var installerUrl = manifest.InstallerUrl?.Trim();
        var sha256 = manifest.Sha256?.Trim();
        var releaseNotesUrl = manifest.ReleaseNotesUrl?.Trim();

        if (string.IsNullOrWhiteSpace(version))
        {
            errorMessage = "update.json の version が空です。";
            return false;
        }

        if (!TryParseVersion(version, out _))
        {
            errorMessage = $"version が不正です: {version}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(installerUrl)
            || !Uri.TryCreate(installerUrl, UriKind.Absolute, out var installerUri)
            || !string.Equals(installerUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"installerUrl は HTTPS の絶対 URL である必要があります: {installerUrl}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(sha256)
            || sha256.Length != 64
            || !sha256.All(static c => char.IsAsciiHexDigit(c)))
        {
            errorMessage = "sha256 は 64 文字の16進数である必要があります。";
            return false;
        }

        normalizedManifest = new AppUpdateManifest
        {
            Version = version,
            InstallerUrl = installerUrl,
            Sha256 = sha256.ToLowerInvariant(),
            PublishedAt = manifest.PublishedAt,
            ReleaseNotesUrl = string.IsNullOrWhiteSpace(releaseNotesUrl) ? null : releaseNotesUrl
        };

        return true;
    }

    private static bool TryParseVersion(string rawVersion, out Version parsedVersion)
    {
        parsedVersion = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var value = rawVersion.Trim();
        var plusIndex = value.IndexOf('+');
        if (plusIndex >= 0)
        {
            value = value[..plusIndex];
        }

        var dashIndex = value.IndexOf('-');
        if (dashIndex >= 0)
        {
            value = value[..dashIndex];
        }

        if (Version.TryParse(value, out var parsed) && parsed is not null)
        {
            parsedVersion = parsed;
            return true;
        }

        parsedVersion = new Version(0, 0, 0);
        return false;
    }

    private static string BuildManifestUrl()
    {
        var separator = ManifestUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{ManifestUrl}{separator}ts={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    private static string GetInstallerTempPath(string version)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "MaiPilot", "updates");
        return Path.Combine(tempRoot, $"MaiPilotSetup-{version}.exe");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsTrustedSignerSubject(string? signerSubject)
    {
        if (string.IsNullOrWhiteSpace(signerSubject))
        {
            return false;
        }

        return signerSubject
            .Split(',')
            .Select(part => part.Trim())
            .Any(part => string.Equals(part, TrustedSignerSubject, StringComparison.OrdinalIgnoreCase));
    }
}
