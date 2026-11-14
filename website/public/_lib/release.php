<?php
declare(strict_types=1);

function jacquard_valid_release_version(string $version): bool
{
    return strlen($version) <= 64 && preg_match('/\A[0-9]{1,9}(?:\.[0-9]{1,9}){1,3}(?:-[A-Za-z0-9][A-Za-z0-9.-]{0,31})?(?:\+[A-Za-z0-9][A-Za-z0-9.-]{0,31})?\z/D', $version) === 1;
}

function jacquard_release_directory(array $config, string $configPath): ?string
{
    $configured = $config['download_dir'] ?? dirname($configPath) . '/releases';
    if (!is_string($configured) || $configured === '') return null;
    if (!is_dir($configured) && !@mkdir($configured, 0700, true) && !is_dir($configured)) return null;
    $real = realpath($configured);
    if ($real === false) return null;
    $webRoot = realpath($_SERVER['DOCUMENT_ROOT'] ?? '');
    if ($webRoot !== false && ($real === $webRoot || str_starts_with($real, $webRoot . DIRECTORY_SEPARATOR))) return null;
    return $real;
}

function jacquard_current_release(array $config, string $configPath): ?array
{
    $directory = jacquard_release_directory($config, $configPath);
    if ($directory === null) return null;
    $manifestPath = $directory . DIRECTORY_SEPARATOR . 'release.json';
    if (!is_file($manifestPath) || !is_readable($manifestPath) || filesize($manifestPath) > 8192) return null;
    $manifest = json_decode((string)file_get_contents($manifestPath), true);
    if (!is_array($manifest)) return null;
    $version = $manifest['version'] ?? null;
    $md5 = $manifest['md5'] ?? null;
    $sha256 = $manifest['sha256'] ?? null;
    $size = $manifest['size'] ?? null;
    if (!is_string($version) || !jacquard_valid_release_version($version) ||
        !is_string($md5) || preg_match('/\A[a-f0-9]{32}\z/iD', $md5) !== 1 ||
        !is_string($sha256) || preg_match('/\A[a-f0-9]{64}\z/iD', $sha256) !== 1 ||
        !is_int($size) || $size < 1 || $size > 200 * 1024 * 1024) return null;
    $installer = 'JacquardStudio-Setup-' . $version . '-' . strtolower($sha256) . '.exe';
    $path = realpath($directory . DIRECTORY_SEPARATOR . $installer);
    if ($path === false || !is_file($path) || !is_readable($path) || !str_starts_with($path, $directory . DIRECTORY_SEPARATOR)) return null;
    if (filesize($path) !== $size) return null;
    return [
        'directory' => $directory,
        'version' => $version,
        'md5' => strtolower($md5),
        'sha256' => strtolower($sha256),
        'size' => $size,
        'installer' => $installer,
        'path' => $path,
        'published_at' => is_string($manifest['published_at'] ?? null) ? $manifest['published_at'] : '',
    ];
}
