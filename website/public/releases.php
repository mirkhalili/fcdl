<?php
declare(strict_types=1);
require __DIR__.'/_lib/bootstrap.php';
require_once __DIR__.'/_lib/release.php';
start_admin_session($config);
header('Cache-Control: no-store');

$error='';
$message=is_string($_SESSION['release_notice'] ?? null) ? $_SESSION['release_notice'] : '';
unset($_SESSION['release_notice']);
$logged=!empty($_SESSION['admin']);

if($_SERVER['REQUEST_METHOD']==='POST') {
 if(!isset($_POST['csrf']) || !is_string($_POST['csrf']) || !hash_equals($_SESSION['csrf'],$_POST['csrf'])) {http_response_code(403);exit('Invalid request.');}
 $action=$_POST['action'] ?? '';
 if($action==='logout' && $logged) {$_SESSION=[];session_destroy();header('Location: admin.php');exit;}
 if($action!=='upload' || !$logged) {http_response_code(403);exit('Forbidden.');}

 $file=$_FILES['installer'] ?? null;
 $versionFile=$_FILES['version_file'] ?? null;
 $md5=$_POST['md5'] ?? '';
 $dir=jacquard_release_directory($config,$configPath);
 if(!is_array($file) || ($file['error'] ?? UPLOAD_ERR_NO_FILE)!==UPLOAD_ERR_OK || !isset($file['tmp_name'],$file['size']) || !is_string($file['tmp_name']) || !is_int($file['size']) || !is_uploaded_file($file['tmp_name']) || $file['size']<1024 || $file['size']>200*1024*1024) {
  $error='بارگذاری ناموفق بود؛ فایل EXE باید حداکثر ۲۰۰ مگابایت و در محدوده تنظیمات PHP باشد.';
 } elseif(!is_array($versionFile) || ($versionFile['error'] ?? UPLOAD_ERR_NO_FILE)!==UPLOAD_ERR_OK || !isset($versionFile['tmp_name'],$versionFile['size']) || !is_string($versionFile['tmp_name']) || !is_int($versionFile['size']) || !is_uploaded_file($versionFile['tmp_name']) || $versionFile['size']<1 || $versionFile['size']>128) {
  $error='فایل version.txt معتبر را نیز بارگذاری کنید؛ اندازه آن باید حداکثر ۱۲۸ بایت باشد.';
 } elseif(!is_string($md5) || preg_match('/\A[a-f0-9]{32}\z/iD',trim($md5))!==1) {
  $error='MD5 باید دقیقاً ۳۲ رقم شانزده‌شانزدهی باشد.';
 } else {
  $stream=fopen($file['tmp_name'],'rb');
  $magic=$stream ? fread($stream,2) : false;
  if($stream)fclose($stream);
  $version=trim((string)file_get_contents($versionFile['tmp_name']));
  if($magic!=='MZ')$error='فایل انتخاب‌شده اجرایی ویندوز نیست.';
  elseif(!jacquard_valid_release_version($version))$error='محتوای version.txt قالب نسخهٔ پشتیبانی‌شده ندارد.';
  elseif($dir===null)$error='پوشه امن انتشار باید موجود یا قابل‌ساخت و خارج از مسیر عمومی وب باشد.';
  else {
   $actualMd5=hash_file('md5',$file['tmp_name']);
   $sha256=hash_file('sha256',$file['tmp_name']);
   if(!is_string($actualMd5) || !is_string($sha256) || !hash_equals(strtolower(trim($md5)),$actualMd5))$error='MD5 واردشده با فایل نصب مطابقت ندارد؛ انتشار لغو شد.';
   else {
    $installer='JacquardStudio-Setup-'.$version.'-'.$sha256.'.exe';
    $target=$dir.DIRECTORY_SEPARATOR.$installer;
    $temp=$dir.DIRECTORY_SEPARATOR.'upload-'.bin2hex(random_bytes(16));
    if(!move_uploaded_file($file['tmp_name'],$temp))$error='انتقال فایل بارگذاری‌شده ممکن نیست.';
    else {
     chmod($temp,0600);
     if(is_file($target) && filesize($target)===$file['size'] && hash_file('sha256',$target)===$sha256) {@unlink($temp);}
     elseif(!rename($temp,$target)) {@unlink($temp);$error='ذخیره امن فایل نسخه‌دار ممکن نیست.';}
     if(!$error) {
      $release=['version'=>$version,'md5'=>$actualMd5,'sha256'=>$sha256,'size'=>$file['size'],'published_at'=>gmdate('Y-m-d\TH:i:s\Z')];
      $json=json_encode($release,JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
      $manifestTemp=$dir.DIRECTORY_SEPARATOR.'.release-'.bin2hex(random_bytes(16)).'.json';
      if(file_put_contents($manifestTemp,$json,LOCK_EX)!==strlen($json)) {@unlink($manifestTemp);$error='ذخیره مشخصات انتشار ممکن نیست.';}
      else {
       chmod($manifestTemp,0600);
       if(!rename($manifestTemp,$dir.DIRECTORY_SEPARATOR.'release.json')) {@unlink($manifestTemp);$error='انتشار مشخصات نسخه ممکن نیست.';}
       else {
        $_SESSION['release_notice']='نسخهٔ '.$version.' با MD5 و SHA-256 بررسی و منتشر شد. صفحهٔ دانلود عمومی اکنون مشخصات همین فایل را نمایش می‌دهد؛ امضای ناشر مستقل بررسی نشده است.';
        header('Location: releases.php');
        exit;
       }
      }
     }
    }
   }
  }
 }
}

$release=$logged ? jacquard_current_release($config,$configPath) : null;
?><!doctype html>
<html lang="fa" dir="rtl">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>مدیریت انتشار | ژاکارد استودیو</title><link rel="stylesheet" href="assets/style.css">
</head>
<body><div class="shell admin-shell">
<header class="header">
 <a class="brand" href="./"><span class="brandmark">م</span><span>میر خلیلی<span class="dot">.</span><small>JACQUARD STUDIO</small></span></a>
 <nav aria-label="راهبری مدیریت"><a href="admin.php">مدیریت مشتریان</a><a href="download-info.php">صفحهٔ دانلود عمومی</a><a href="./">صفحه اصلی</a></nav>
 <?php if($logged):?><form method="post"><input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="logout"><button class="nav-link" type="submit">خروج ↗</button></form><?php endif;?>
</header>
<main class="admin-main release-admin-main">
<?php if(!$logged):?>
 <div class="login-card"><span class="eyebrow">بخش انتشار</span><h1>ورود مدیر لازم است.</h1><p>انتشار فایل از مدیریت مشتریان جداست و فقط پس از ورود مدیر در دسترس قرار می‌گیرد.</p><a class="button primary" href="admin.php">ورود به پنل مدیریت ←</a></div>
<?php else:?>
 <div class="admin-title"><div><span class="eyebrow">انتشار نرم‌افزار</span><h1>مدیریت نسخه‌ها<span class="dot">.</span></h1><p>انتشار فایل نصب و مدیریت مشتریان در دو بخش جدا انجام می‌شود.</p></div><span class="badge">● &nbsp; انتشار امن</span></div>
 <?php if($message):?><p class="notice"><?=esc($message)?></p><?php endif;?>
 <?php if($error):?><p class="alert"><?=esc($error)?></p><?php endif;?>
 <section class="table-card release-card">
  <div class="table-heading"><div><span class="eyebrow">صفحهٔ عمومی دانلود</span><h2>انتشار نسخهٔ تازه</h2></div><a href="download-info.php">پیش‌نمایش صفحهٔ دانلود ↙</a></div>
  <p>فایل نصب EXE (حداکثر ۲۰۰ مگابایت)، فایل <code>version.txt</code> همان ساخت و MD5 فایل نصب را بارگذاری کنید. سایت اندازه، MD5 و SHA-256 را بررسی می‌کند و فایل را بیرون وب‌روت نگه می‌دارد. checksum به‌تنهایی امضای ناشر نیست.</p>
  <?php if($release):?>
   <div class="release-current"><strong>نسخهٔ فعال: <bdi><?=esc($release['version'])?></bdi></strong><span>نام فایل: <code dir="ltr"><?=esc($release['filename'])?></code></span><span>MD5: <code dir="ltr"><?=esc($release['md5'])?></code></span><span>به‌روزرسانی: <bdi><?=esc($release['published_at'])?> UTC</bdi></span></div>
  <?php else:?>
   <p class="release-empty">هنوز انتشار معتبری ثبت نشده است. پس از بارگذاری موفق، مشخصات نسخه در صفحهٔ عمومی نمایش داده می‌شود.</p>
  <?php endif;?>
  <form method="post" enctype="multipart/form-data" class="release-form">
   <input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="upload">
   <label class="upload-field"><span>فایل نصب برنامه <small>EXE · حداکثر ۲۰۰ مگابایت</small></span><input type="file" name="installer" accept=".exe,application/vnd.microsoft.portable-executable" required></label>
   <label class="upload-field"><span>فایل نسخه <small>version.txt · حداکثر ۱۲۸ بایت</small></span><input type="file" name="version_file" accept=".txt,text/plain" required></label>
   <label class="upload-field md5-field"><span>MD5 فایل نصب <small>۳۲ رقم شانزده‌شانزدهی</small></span><input type="text" name="md5" dir="ltr" inputmode="text" autocomplete="off" spellcheck="false" pattern="[A-Fa-f0-9]{32}" minlength="32" maxlength="32" placeholder="MD5 installer" required></label>
   <button class="button primary" type="submit">بررسی و انتشار نسخه <span>↙</span></button>
  </form>
 </section>
 <section class="release-help"><h2>پس از انتشار چه می‌شود؟</h2><ol><li>صفحهٔ عمومی نام فایل، نسخه، تاریخ انتشار و MD5 را نشان می‌دهد.</li><li>پیوند دریافت مستقیم همان نصب‌کننده را از <code>download.php</code> می‌گیرد.</li><li>نسخهٔ قبلی تا ثبت موفق manifest جدید دست‌نخورده می‌ماند.</li></ol></section>
<?php endif;?>
</main>
<footer><p>ژاکارد استودیو · مدیریت انتشار</p><span>مدیریت مشتریان: <a href="admin.php">ورود به پنل</a></span></footer>
</div></body></html>
