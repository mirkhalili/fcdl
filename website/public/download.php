<?php
declare(strict_types=1);
require_once __DIR__.'/_lib/release.php';

$configPath=getenv('JACQUARD_CONFIG') ?: dirname(__DIR__,2).'/jacquard-private/config.php';
header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: no-referrer');
header('Cache-Control: no-store');
if(!is_file($configPath)){http_response_code(503);header('Content-Type: text/plain; charset=utf-8');exit('سامانه دانلود هنوز پیکربندی نشده است.');}
$config=require $configPath;
if(!is_array($config)){http_response_code(503);header('Content-Type: text/plain; charset=utf-8');exit('تنظیمات دانلود معتبر نیست.');}
$isHttps=isset($_SERVER['HTTPS']) && $_SERVER['HTTPS']!=='off';
$local=($config['allow_local_http'] ?? false)===true && in_array($_SERVER['REMOTE_ADDR'] ?? '',['127.0.0.1','::1'],true);
if(!$isHttps && !$local){http_response_code(403);header('Content-Type: text/plain; charset=utf-8');exit('HTTPS required.');}

if(($_GET['manifest'] ?? '')==='1'){
    $release=jacquard_current_release($config,$configPath);
    if($release===null){http_response_code(404);header('Content-Type: application/json; charset=utf-8');echo json_encode(['product'=>'JacquardStudio','available'=>false],JSON_UNESCAPED_SLASHES);exit;}
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode([
        'product'=>'JacquardStudio',
        'version'=>$release['version'],
        'md5'=>$release['md5'],
        'sha256'=>$release['sha256'],
        'size'=>$release['size'],
        'publishedAt'=>$release['published_at'],
        'download'=>'download.php',
    ],JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
    exit;
}

$release=jacquard_current_release($config,$configPath);
if($release!==null){$file=$release['path'];$filename='JacquardStudio-Setup-'.$release['version'].'.exe';}
else {
    // Keep the public download link working for a pre-manifest installation until its first managed release.
    $directory=jacquard_release_directory($config,$configPath);
    $file=$directory===null ? false : $directory.DIRECTORY_SEPARATOR.'JacquardStudio-Setup.exe';
    $filename='JacquardStudio-Setup.exe';
    if($file===false || !is_file($file) || !is_readable($file)){http_response_code(503);header('Content-Type: text/plain; charset=utf-8');exit('فایل دانلود هنوز منتشر نشده است.');}
}
$size=filesize($file);
if(!is_int($size) || $size<1 || $size>200*1024*1024){http_response_code(503);header('Content-Type: text/plain; charset=utf-8');exit('فایل انتشار معتبر نیست.');}
header('Content-Type: application/octet-stream');
header('Content-Disposition: attachment; filename="'.$filename.'"');
header('Content-Length: '.$size);
if(($_SERVER['REQUEST_METHOD'] ?? 'GET')!=='HEAD')readfile($file);
