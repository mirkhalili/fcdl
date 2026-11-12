<?php
declare(strict_types=1);
require __DIR__.'/_lib/bootstrap.php';start_admin_session($config);
header('Cache-Control: no-store');
$error='';$message='';
if($_SERVER['REQUEST_METHOD']==='POST') {
 if (!isset($_POST['csrf']) || !is_string($_POST['csrf']) || !hash_equals($_SESSION['csrf'],$_POST['csrf'])) {http_response_code(403);exit('Invalid request.');}
 $action=$_POST['action'] ?? '';
 if($action==='login') {
  if(!limit($db,'login:'.hash('sha256',ip()),8,900))$error='تعداد تلاش‌ها بیش از حد مجاز است؛ بعداً دوباره تلاش کنید.';
  elseif(!is_string($_POST['username'] ?? null) || strlen($_POST['username'])>128 || !hash_equals((string)($config['admin_username'] ?? 'admin'),$_POST['username']) || !is_string($_POST['password'] ?? null) || strlen($_POST['password'])>1024 || !password_verify($_POST['password'],$config['admin_hash'])) $error='ورود ناموفق بود.';
  else {session_regenerate_id(true);$_SESSION['admin']=true;$_SESSION['csrf']=bin2hex(random_bytes(32));header('Location: admin.php');exit;}
 } elseif($action==='logout' && !empty($_SESSION['admin'])) {$_SESSION=[];session_destroy();header('Location: admin.php');exit;}
 elseif($action==='upload' && !empty($_SESSION['admin'])) {
  $file=$_FILES['installer'] ?? null;$dir=$config['download_dir'] ?? dirname($configPath).'/releases';
  if(!is_array($file) || ($file['error'] ?? UPLOAD_ERR_NO_FILE)!==UPLOAD_ERR_OK || !isset($file['tmp_name'],$file['size']) || !is_string($file['tmp_name']) || !is_uploaded_file($file['tmp_name']) || $file['size']<1024 || $file['size']>200*1024*1024) {$error='بارگذاری ناموفق بود؛ فایل EXE باید حداکثر ۲۰۰ مگابایت و در محدوده تنظیمات PHP باشد.';}
  else {
   $stream=fopen($file['tmp_name'],'rb');$magic=$stream ? fread($stream,2) : false;if($stream)fclose($stream);
   if($magic!=='MZ')$error='فایل انتخاب‌شده اجرایی ویندوز نیست.';
   else {
    if(!is_dir($dir) && !mkdir($dir,0700,true) && !is_dir($dir)){$error='ساخت پوشه امن دانلود ممکن نیست.';}
    else {
     $real=realpath($dir);$web=realpath($_SERVER['DOCUMENT_ROOT'] ?? '');
     if(!$real || !$web || $real===$web || str_starts_with($real,$web.DIRECTORY_SEPARATOR))$error='پوشه دانلود باید خارج از مسیر عمومی وب باشد.';
     else { $tmp=$real.'/upload-'.bin2hex(random_bytes(16));$target=$real.'/JacquardStudio-Setup.exe';
      if(!move_uploaded_file($file['tmp_name'],$tmp))$error='انتقال فایل بارگذاری‌شده ممکن نیست.';
      else {chmod($tmp,0600);if(!rename($tmp,$target)){@unlink($tmp);$error='انتشار فایل ممکن نیست.';}else $message='فایل منتشر شد. پیش از انتشار، اصالت و سلامت فایل اجرایی را خودتان بررسی کنید.';}
     }
    }
   }
  }
 }
 elseif($action==='status' && !empty($_SESSION['admin'])) {
  $id=$_POST['id'] ?? ''; $status=$_POST['status'] ?? '';if(!is_string($id) || !preg_match('/^[a-f0-9-]{36}$/D',$id) || !in_array($status,['pending','approved','blocked'],true)) {http_response_code(400);exit('Invalid request.');}
  $s=$db->prepare('UPDATE installations SET status=? WHERE id=?');$s->execute([$status,$id]);$message='وضعیت درخواست ثبت شد. نتیجه در ارتباط بعدی برنامه اعمال می‌شود؛ برای صدور، کلید خصوصی معتبر باید تنظیم شده باشد.';
 }
}
$logged=!empty($_SESSION['admin']);$items=[];$stats=['total'=>0,'approved'=>0,'pending'=>0,'blocked'=>0];
if($logged) {
 $stats['total']=(int)$db->query('SELECT COUNT(*) FROM installations')->fetchColumn();
 foreach($db->query('SELECT status,COUNT(*) AS n FROM installations GROUP BY status') as $row)$stats[$row['status']]=(int)$row['n'];
 $items=$db->query('SELECT id,device_hash,status,created_at,last_seen,last_ip,app_version,os_version,cpu_model,ram_bytes,machine_name FROM installations ORDER BY last_seen DESC LIMIT 200')->fetchAll(PDO::FETCH_ASSOC);
}
$labels=['pending'=>'در انتظار بررسی','approved'=>'تأیید درخواست','blocked'=>'مسدود در پنل'];
?><!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>پنل مدیریت | سام</title><link rel="stylesheet" href="assets/style.css"></head><body><div class="shell admin-shell"><header class="header"><a class="brand" href="./"><span class="brandmark">م</span><span>میر خلیلی<span class="dot">.</span><small>JACQUARD STUDIO</small></span></a><nav><a href="./">صفحه اصلی</a></nav><?php if($logged):?><form method="post"><input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="logout"><button class="nav-link" type="submit">خروج ↗</button></form><?php endif;?></header>
<main class="admin-main"><?php if(!$logged):?><div class="login-card"><span class="eyebrow">مدیریت سامانه</span><h1>خوش آمدید.</h1><p>برای مشاهده درخواست‌ها، رمز مدیریت را وارد کنید.</p><?php if($error):?><p class="alert"><?=esc($error)?></p><?php endif;?><form method="post"><input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="login"><label for="username">نام کاربری</label><input id="username" type="text" name="username" autocomplete="username" required maxlength="128" dir="ltr"><label for="password">رمز مدیریت</label><input id="password" type="password" name="password" autocomplete="current-password" required maxlength="1024"><button class="button primary" type="submit">ورود به پنل ←</button></form></div>
<?php else:?><div class="admin-title"><div><span class="eyebrow">نمای کلی</span><h1>دستگاه‌های شما<span class="dot">.</span></h1><p>آخرین گزارش‌ها و درخواست‌های ثبت‌شده در یک نگاه.</p></div><span class="badge">● &nbsp; پنل مدیریت</span></div><p class="notice">تأیید درخواست، مجوز هفت‌روزه برای همان دستگاه صادر می‌کند. لغو در اتصال بعدی اعمال می‌شود؛ کلید خصوصی معتبر و نسخه دسکتاپ هماهنگ لازم‌اند.</p><?php if($message):?><p class="notice"><?=esc($message)?></p><?php endif;?><?php if($error):?><p class="alert"><?=esc($error)?></p><?php endif;?><div class="stats"><article><span>همه دستگاه‌ها</span><strong><?=esc((string)$stats['total'])?></strong></article><article><span>در انتظار بررسی</span><strong><?=esc((string)$stats['pending'])?></strong></article><article><span>تأیید درخواست</span><strong><?=esc((string)$stats['approved'])?></strong></article><article><span>مسدود در پنل</span><strong><?=esc((string)$stats['blocked'])?></strong></article></div>
<div class="table-card upload-card"><div class="table-heading"><h2>بارگذاری فایل نصب</h2><a href="download.php">لینک دریافت عمومی ↙</a></div><p>فقط فایل نصب EXE ویندوز، حداکثر ۲۰۰ مگابایت. فایل قبلی پس از بارگذاری موفق جایگزین می‌شود. اعتبار و امضای فایل را پیش از بارگذاری بررسی کنید.</p><form method="post" enctype="multipart/form-data"><input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="upload"><input type="file" name="installer" accept=".exe,application/vnd.microsoft.portable-executable" required><button class="button primary" type="submit">بارگذاری و انتشار</button></form></div>
<div class="table-card"><div class="table-heading"><h2>آخرین دستگاه‌ها</h2><span>حداکثر ۲۰۰ مورد</span></div><div class="table-scroll"><table><thead><tr><th>شناسه نرم‌افزار / دستگاه</th><th>سیستم</th><th>آخرین اجرا و IP</th><th>وضعیت درخواست</th><th>مدیریت درخواست</th></tr></thead><tbody><?php foreach($items as $item):?><tr><td><code><?=esc($item['id'])?></code><br><small>دستگاه: <?=esc($item['device_hash'])?></small></td><td><?=esc($item['machine_name'])?><br><small><?=esc($item['cpu_model'])?> · <?=esc($item['os_version'])?><br><?=esc($item['app_version'])?> · <?=esc((string)round((float)$item['ram_bytes']/1073741824,1))?> GB RAM</small></td><td><?=esc($item['last_seen'])?> UTC<br><small><?=esc($item['last_ip'])?></small></td><td><span class="status"><?=esc($labels[$item['status']] ?? '')?></span></td><td><form method="post" class="status-form"><input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="status"><input type="hidden" name="id" value="<?=esc($item['id'])?>"><select name="status"><?php foreach($labels as $value=>$label):?><option value="<?=esc($value)?>" <?=$item['status']===$value?'selected':''?>><?=esc($label)?></option><?php endforeach;?></select><button type="submit">ثبت</button></form></td></tr><?php endforeach;?></tbody></table><?php if(!$items):?><p class="empty">هنوز گزارشی دریافت نشده است.</p><?php endif;?></div></div><?php endif;?></main><footer><p>ژاکارد استودیو · پنل مدیریت</p><span>نسخه آزمایشی</span></footer></div></body></html>
