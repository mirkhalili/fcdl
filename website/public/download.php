<?php
declare(strict_types=1);
$configPath=getenv('JACQUARD_CONFIG') ?: dirname(__DIR__,2).'/jacquard-private/config.php';
if(!is_file($configPath)){http_response_code(503);header('Content-Type: text/plain; charset=utf-8');exit('سامانه دانلود هنوز پیکربندی نشده است.');}
$config=require $configPath;
$dir=$config['download_dir'] ?? dirname($configPath).'/releases';$file=$dir.'/JacquardStudio-Setup.exe';
if(!is_file($file) || !is_readable($file)){http_response_code(503);header('Content-Type: text/plain; charset=utf-8');exit('فایل دانلود هنوز منتشر نشده است.');}
header('Content-Type: application/octet-stream');header('Content-Disposition: attachment; filename="JacquardStudio-Setup.exe"');header('X-Content-Type-Options: nosniff');header('Content-Length: '.filesize($file));readfile($file);
