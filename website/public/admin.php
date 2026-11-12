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
 elseif($action==='save_device' && !empty($_SESSION['admin'])) {
  $device=$_POST['device'] ?? ''; $status=$_POST['status'] ?? ''; $date=$_POST['expiry'] ?? '';
  if(!is_string($device) || !preg_match('/^[A-F0-9]{64}$/D',$device) || !in_array($status,['pending','approved','blocked'],true) || !is_string($date)) {http_response_code(400);exit('Invalid request.');}
  $values=[];foreach(['customer_name'=>120,'phone'=>60,'city'=>100,'notes'=>500] as $field=>$max){$v=$_POST[$field] ?? '';if(!is_string($v) || strlen($v)>$max || preg_match('/[\x00-\x1f\x7f]/',$v) || !preg_match('//u',$v)){http_response_code(400);exit('Invalid field.');}$values[]=trim($v);}
  $expiry=null;if($date!==''){$d=DateTimeImmutable::createFromFormat('!Y-m-d',$date,new DateTimeZone('UTC'));if(!$d || $d->format('Y-m-d')!==$date || $date<'2000-01-01' || $date>'2100-12-30'){http_response_code(400);exit('Invalid date.');}$expiry=$d->modify('+1 day')->format('Y-m-d H:i:s');}
  $s=$db->prepare('UPDATE devices SET status=?,expires_at=?,customer_name=?,phone=?,city=?,notes=? WHERE device_hash=?');$s->execute([$status,$expiry,...$values,$device]);$message='اطلاعات دستگاه ذخیره شد. اعتبار تا پایان روز انتخابی به وقت UTC است.';
 } elseif($action==='delete_device' && !empty($_SESSION['admin'])) {
  $device=$_POST['device'] ?? '';if(!is_string($device) || !preg_match('/^[A-F0-9]{64}$/D',$device)){http_response_code(400);exit('Invalid request.');}
  $s=$db->prepare('DELETE FROM devices WHERE device_hash=?');$s->execute([$device]);$message='دستگاه، نصب‌ها و تاریخچه آن حذف شد. مجوز آفلاین فقط تا پایان مهلت موجود معتبر می‌ماند.';
 }

}
$logged=!empty($_SESSION['admin']);$items=[];$stats=['total'=>0,'approved'=>0,'pending'=>0,'blocked'=>0];$detail=null;$events=[];$confirmDelete=false;
$labels=['pending'=>'در انتظار بررسی','approved'=>'فعال','blocked'=>'مسدود'];
if($logged) {
 $stats['total']=(int)$db->query('SELECT COUNT(*) FROM devices')->fetchColumn();
 foreach($db->query('SELECT status,COUNT(*) AS n FROM devices GROUP BY status') as $row)$stats[$row['status']]=(int)$row['n'];
 $items=$db->query('SELECT d.*, (SELECT MAX(i.last_seen) FROM installations i WHERE i.device_hash=d.device_hash) AS last_seen, (SELECT COUNT(*) FROM installations i WHERE i.device_hash=d.device_hash) AS installations_count FROM devices d ORDER BY COALESCE(last_seen,d.created_at) DESC LIMIT 200')->fetchAll(PDO::FETCH_ASSOC);
 $device=$_GET['device'] ?? '';if(is_string($device) && preg_match('/^[A-F0-9]{64}$/D',$device)){
  $s=$db->prepare('SELECT * FROM devices WHERE device_hash=?');$s->execute([$device]);$detail=$s->fetch(PDO::FETCH_ASSOC) ?: null;
  if($detail){$s=$db->prepare('SELECT e.* FROM execution_events e JOIN installations i ON i.id=e.installation_id WHERE i.device_hash=? ORDER BY e.observed_at DESC,e.id DESC LIMIT 500');$s->execute([$device]);$events=$s->fetchAll(PDO::FETCH_ASSOC);$confirmDelete=($_GET['delete'] ?? '')==='1';}
 }
}
?><!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>پنل مدیریت | سام</title><link rel="stylesheet" href="assets/style.css"></head><body><div class="shell admin-shell"><header class="header"><a class="brand" href="./"><span class="brandmark">م</span><span>میر خلیلی<span class="dot">.</span><small>JACQUARD STUDIO</small></span></a><nav><a href="./">صفحه اصلی</a></nav><?php if($logged):?><form method="post"><input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="logout"><button class="nav-link" type="submit">خروج ↗</button></form><?php endif;?></header>
<main class="admin-main"><?php if(!$logged):?><div class="login-card"><span class="eyebrow">مدیریت سامانه</span><h1>خوش آمدید.</h1><p>برای مشاهده درخواست‌ها، رمز مدیریت را وارد کنید.</p><?php if($error):?><p class="alert"><?=esc($error)?></p><?php endif;?><form method="post"><input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="login"><label for="username">نام کاربری</label><input id="username" type="text" name="username" autocomplete="username" required maxlength="128" dir="ltr"><label for="password">رمز مدیریت</label><input id="password" type="password" name="password" autocomplete="current-password" required maxlength="1024"><button class="button primary" type="submit">ورود به پنل ←</button></form></div>
<?php else:?><div class="admin-title"><div><span class="eyebrow">نمای کلی</span><h1>دستگاه‌های شما<span class="dot">.</span></h1><p>آخرین گزارش‌ها و درخواست‌های ثبت‌شده در یک نگاه.</p></div><span class="badge">● &nbsp; پنل مدیریت</span></div><p class="notice">تأیید درخواست، مجوز هفت‌روزه برای همان دستگاه صادر می‌کند. لغو در اتصال بعدی اعمال می‌شود؛ کلید خصوصی معتبر و نسخه دسکتاپ هماهنگ لازم‌اند.</p><?php if($message):?><p class="notice"><?=esc($message)?></p><?php endif;?><?php if($error):?><p class="alert"><?=esc($error)?></p><?php endif;?><div class="stats"><article><span>همه دستگاه‌ها</span><strong><?=esc((string)$stats['total'])?></strong></article><article><span>در انتظار بررسی</span><strong><?=esc((string)$stats['pending'])?></strong></article><article><span>تأییدشده (شامل منقضی)</span><strong><?=esc((string)$stats['approved'])?></strong></article><article><span>مسدود در پنل</span><strong><?=esc((string)$stats['blocked'])?></strong></article></div>
<div class="table-card upload-card"><div class="table-heading"><h2>بارگذاری فایل نصب</h2><a href="download.php">لینک دریافت عمومی ↙</a></div><p>فقط فایل نصب EXE ویندوز، حداکثر ۲۰۰ مگابایت. فایل قبلی پس از بارگذاری موفق جایگزین می‌شود. اعتبار و امضای فایل را پیش از بارگذاری بررسی کنید.</p><form method="post" enctype="multipart/form-data"><input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="upload"><input type="file" name="installer" accept=".exe,application/vnd.microsoft.portable-executable" required><button class="button primary" type="submit">بارگذاری و انتشار</button></form></div>
<?php if($detail):?><div class="table-card device-detail"><div class="table-heading"><h2>ویرایش دستگاه و تاریخچه اتصال</h2><a href="admin.php">بازگشت به فهرست</a></div><p>شناسه دستگاه: <code><?=esc($detail['device_hash'])?></code></p>
<?php if($confirmDelete):?><p class="notice">حذف، تمام نصب‌ها و تاریخچه این دستگاه را نیز حذف می‌کند. تأیید می‌کنید؟ حذف مجوز آفلاین را فوراً باطل نمی‌کند.</p><form method="post"><input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="delete_device"><input type="hidden" name="device" value="<?=esc($detail['device_hash'])?>"><button class="button primary" type="submit">بله، حذف شود</button> <a href="?device=<?=esc($detail['device_hash'])?>">انصراف</a></form>
<?php else:?>
<form method="post" class="device-form"><input type="hidden" name="csrf" value="<?=esc($_SESSION['csrf'])?>"><input type="hidden" name="action" value="save_device"><input type="hidden" name="device" value="<?=esc($detail['device_hash'])?>">
<label>نام کاربر <input name="customer_name" maxlength="30" value="<?=esc($detail['customer_name'])?>"></label><label>تلفن <input name="phone" maxlength="30" value="<?=esc($detail['phone'])?>"></label><label>شهر <input name="city" maxlength="25" value="<?=esc($detail['city'])?>"></label><label>توضیحات <input name="notes" maxlength="125" value="<?=esc($detail['notes'])?>"></label>
<label>وضعیت <select name="status"><?php foreach($labels as $value=>$label):?><option value="<?=esc($value)?>" <?=$detail['status']===$value?'selected':''?>><?=esc($label)?></option><?php endforeach;?></select></label>
<?php $expiry=$detail['expires_at'] ? (new DateTimeImmutable($detail['expires_at'],new DateTimeZone('UTC')))->modify('-1 day')->format('Y-m-d') : '';?><label>آخرین روز اعتبار (میلادی، UTC) <input type="date" name="expiry" min="2000-01-01" max="2100-12-30" value="<?=esc($expiry)?>"></label><p>خالی یعنی بدون تاریخ پایان؛ مجوز آفلاین در هر حال حداکثر هفت روزه است.</p><button class="button primary" type="submit">ذخیره تغییرات</button> <a class="delete-link" href="?device=<?=esc($detail['device_hash'])?>&amp;delete=1">حذف دستگاه</a></form>
<h3>تاریخچه اتصال</h3><p>آخرین ۵۰۰ اتصال؛ زمان‌ها UTC هستند. داده‌های قدیمی ممکن است جزئیات سیستم نداشته باشند.</p><div class="table-scroll"><table><thead><tr><th>تاریخ</th><th>IP</th><th>سیستم‌عامل / نسخه</th><th>دستگاه / پردازنده / RAM</th><th>شناسه نصب</th></tr></thead><tbody><?php foreach($events as $event):?><tr><td><?=esc($event['observed_at'])?></td><td><?=esc($event['ip'])?></td><td><?=esc($event['os_version'])?><br><?=esc($event['app_version'])?></td><td><?=esc($event['machine_name'])?><br><?=esc($event['cpu_model'])?> · <?=esc((string)round((float)$event['ram_bytes']/1073741824,1))?> GB</td><td><small><?=esc($event['installation_id'])?></small></td></tr><?php endforeach;?></tbody></table><?php if(!$events):?><p class="empty">اتصالی ثبت نشده است.</p><?php endif;?></div>
<?php endif;?></div><?php endif;?>
<div class="table-card"><div class="table-heading"><h2>دستگاه‌ها</h2><span>حداکثر ۲۰۰ مورد</span></div><div class="table-scroll"><table><thead><tr><th>شناسه دستگاه</th><th>نام کاربر</th><th>تلفن</th><th>شهر</th><th>وضعیت / اعتبار</th><th>آخرین اتصال</th><th>مدیریت</th></tr></thead><tbody><?php foreach($items as $item):?><tr><td><code><?=esc($item['device_hash'])?></code></td><td><?=esc($item['customer_name'])?></td><td><?=esc($item['phone'])?></td><td><?=esc($item['city'])?></td><td><span class="status"><?=esc($item['status']==='approved' && $item['expires_at']!==null && (new DateTimeImmutable($item['expires_at'],new DateTimeZone('UTC')))->getTimestamp()<=time() ? 'منقضی' : ($labels[$item['status']] ?? ''))?></span><br><small><?php if($item['expires_at']):?>تا <?=esc((new DateTimeImmutable($item['expires_at'],new DateTimeZone('UTC')))->modify('-1 day')->format('Y-m-d'))?> UTC<?php else:?>بدون تاریخ پایان<?php endif;?></small></td><td><?=esc($item['last_seen'] ?? '—')?> UTC</td><td><a href="?device=<?=esc($item['device_hash'])?>">ویرایش و جزئیات اتصال</a></td></tr><?php endforeach;?></tbody></table><?php if(!$items):?><p class="empty">هنوز دستگاهی ثبت نشده است.</p><?php endif;?></div></div><?php endif;?></main><footer><p>ژاکارد استودیو · پنل مدیریت</p><span>نسخه آزمایشی</span></footer></div></body></html>
