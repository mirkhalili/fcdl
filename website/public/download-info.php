<?php
declare(strict_types=1);
require_once __DIR__.'/_lib/release.php';
require_once __DIR__.'/_lib/jalali.php';

$configPath=getenv('JACQUARD_CONFIG') ?: dirname(__DIR__,2).'/jacquard-private/config.php';
header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: no-referrer');
header('X-Frame-Options: DENY');
header("Content-Security-Policy: default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'");
header('Cache-Control: no-store');
if(!is_file($configPath)){http_response_code(503);header('Content-Type: text/plain; charset=utf-8');exit('سامانه دانلود هنوز پیکربندی نشده است.');}
$config=require $configPath;
if(!is_array($config)){http_response_code(503);header('Content-Type: text/plain; charset=utf-8');exit('تنظیمات دانلود معتبر نیست.');}
$isHttps=isset($_SERVER['HTTPS']) && $_SERVER['HTTPS']!=='off';
$local=($config['allow_local_http'] ?? false)===true && in_array($_SERVER['REMOTE_ADDR'] ?? '',['127.0.0.1','::1'],true);
if(!$isHttps && !$local){http_response_code(403);header('Content-Type: text/plain; charset=utf-8');exit('HTTPS required.');}
$release=jacquard_current_release($config,$configPath);
$escape=static fn(?string $value): string => htmlspecialchars($value ?? '',ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');
$date=$release===null ? '—' : jacquard_format_jalali_date($release['published_at'],true);
header('Content-Type: text/html; charset=utf-8');
?><!doctype html>
<html lang="fa" dir="rtl">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="description" content="دریافت امن Jacquard Studio و بررسی مشخصات نسخهٔ منتشرشده">
<title>دانلود Jacquard Studio | میر خلیلی</title><link rel="stylesheet" href="assets/style.css">
</head>
<body><div class="shell download-shell">
<header class="header">
 <a class="brand" href="./"><span class="brandmark">م</span><span>میر خلیلی<span class="dot">.</span><small>JACQUARD STUDIO</small></span></a>
 <nav aria-label="راهبری اصلی"><a href="./#features">امکانات</a><a href="./#workflow">روند طراحی</a><a href="download-info.php" aria-current="page">دریافت برنامه</a></nav>
 <a class="nav-link" href="admin.php">ورود مدیر <span>↗</span></a>
</header>
<main class="public-download">
 <section class="download-intro"><span class="eyebrow">نسخهٔ رسمی ویندوز</span><h1>دریافت Jacquard Studio<span class="dot">.</span></h1><p>مشخصات فایل نصب را پیش از دریافت بررسی کنید. فایل اجرایی از مسیر دانلود امن سایت ارائه می‌شود.</p></section>
 <?php if($release!==null):?>
 <section class="release-download-card" aria-labelledby="release-heading">
  <div class="release-download-heading"><div><span class="eyebrow">نسخهٔ منتشرشده</span><h2 id="release-heading">نسخهٔ <bdi><?= $escape($release['version']) ?></bdi></h2></div><span class="badge">● &nbsp; آمادهٔ دریافت</span></div>
  <dl class="release-metadata">
   <div><dt>نام فایل</dt><dd><code dir="ltr"><?= $escape($release['filename']) ?></code></dd></div>
   <div><dt>نسخه</dt><dd><bdi><?= $escape($release['version']) ?></bdi></dd></div>
   <div><dt>آخرین به‌روزرسانی (شمسی، UTC)</dt><dd><bdi><?= $escape($date) ?></bdi></dd></div>
   <div><dt>MD5</dt><dd><code dir="ltr"><?= $escape($release['md5']) ?></code></dd></div>
   <div><dt>SHA-256</dt><dd><code dir="ltr"><?= $escape($release['sha256']) ?></code></dd></div>
   <div><dt>حجم فایل</dt><dd><bdi><?= $escape(number_format($release['size']/1024/1024,1)) ?> MiB</bdi></dd></div>
  </dl>
  <div class="download-actions"><a class="button primary" href="download.php">دانلود مستقیم فایل نصب <span>↓</span></a><a class="button outline" href="./">بازگشت به صفحهٔ اصلی <span>↙</span></a></div>
  <p class="download-integrity-note">چک‌سام‌ها برای شناسایی خرابی انتقال مفیدند؛ چون از همین سایت دریافت می‌شوند، جایگزین امضای دیجیتال ناشر یا تأیید مستقل اصالت نیستند.</p>
 </section>
 <?php else:?>
 <section class="release-download-card release-not-ready"><span class="eyebrow">در انتظار انتشار</span><h2>فایل نصب هنوز آماده نیست.</h2><p>پس از ثبت نسخهٔ معتبر در پنل انتشار، نام فایل، شماره نسخه، زمان به‌روزرسانی و MD5 در همین صفحه نمایش داده خواهد شد.</p><a class="button outline" href="./">بازگشت به صفحهٔ اصلی <span>↙</span></a></section>
 <?php endif;?>
</main>
<footer><a class="brand" href="./"><span class="brandmark">م</span><span>میر خلیلی<span class="dot">.</span><small>JACQUARD STUDIO</small></span></a><p>پیش از استفادهٔ صنعتی، خروجی‌ها را روی دستگاه موردنظر جداگانه بررسی کنید.</p><span>© Jacquard Studio</span></footer>
</div></body></html>
