<?php
declare(strict_types=1);
// The file must be placed manually after verification; this endpoint never accepts a path from a user.
$file=__DIR__.'/releases/JacquardStudio-Setup.exe';
if(!is_file($file) || !is_readable($file)){http_response_code(503);header('Content-Type: text/plain; charset=utf-8');exit('فایل دانلود هنوز منتشر نشده است.');}
header('Content-Type: application/octet-stream');header('Content-Disposition: attachment; filename="JacquardStudio-Setup.exe"');header('X-Content-Type-Options: nosniff');header('Content-Length: '.filesize($file));readfile($file);
