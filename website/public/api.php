<?php
declare(strict_types=1);
require __DIR__.'/_lib/bootstrap.php';
header('Content-Type: application/json; charset=utf-8');header('Cache-Control: no-store');
function reply(int $status,array $body): never {http_response_code($status);echo json_encode($body,JSON_UNESCAPED_UNICODE);exit;}
if ($_SERVER['REQUEST_METHOD']!=='POST') reply(405,['error'=>'method']);
if ((int)($_SERVER['CONTENT_LENGTH'] ?? 0)>8192) reply(413,['error'=>'size']);
if (!limit($db,'api:'.hash('sha256',ip()),60,3600)) reply(429,['error'=>'rate']);
$raw=file_get_contents('php://input',false,null,0,8193);if(strlen($raw)>8192)reply(413,['error'=>'size']);
$data=json_decode($raw,true,16);if(!is_array($data))reply(400,['error'=>'json']);
$id=$data['installationId'] ?? ''; $secret=$data['secret'] ?? ''; $device=$data['device'] ?? '';
if(!is_string($id) || !preg_match('/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/D',$id) || !is_string($secret) || !preg_match('/^[a-f0-9]{64}$/D',$secret) || !is_string($device) || !preg_match('/^[A-F0-9]{64}$/D',$device))reply(400,['error'=>'identity']);
function field(array $input,string $key,int $max): string { $v=$input[$key] ?? ''; if(!is_string($v) || strlen($v)>$max || preg_match('/[\x00-\x1f\x7f]/',$v))reply(400,['error'=>'field']);return $v; }
$version=field($data,'appVersion',50);$os=field($data,'osVersion',150);$cpu=field($data,'cpuModel',160);$machine=field($data,'machineName',100);
$ram=$data['ramBytes'] ?? 0;if(!is_int($ram) || $ram<0 || $ram>1125899906842624)reply(400,['error'=>'ram']);
function issueToken(array $config,string $device,string $id): ?string {
 $path=$config['license_private_key'] ?? '';if(!is_string($path) || !is_file($path) || !is_readable($path))return null;
 $pem=file_get_contents($path);if($pem===false || strlen($pem)>8192)return null;$key=openssl_pkey_get_private($pem);if($key===false)return null;
 $details=openssl_pkey_get_details($key);if(($details['type'] ?? null)!==OPENSSL_KEYTYPE_EC || ($details['ec']['curve_name'] ?? '')!=='prime256v1')return null;
 $now=time();$payload=json_encode(['version'=>1,'product'=>'JacquardStudio','device'=>$device,'licenseId'=>$id,'issuedAt'=>$now,'notBefore'=>$now-60,'expiresAt'=>$now+604800],JSON_UNESCAPED_SLASHES);
 if($payload===false || !openssl_sign($payload,$signature,$key,OPENSSL_ALGO_SHA256))return null;
 $b64=static fn(string $v): string => rtrim(strtr(base64_encode($v),'+/','-_'),'=');return 'JSL1.'.$b64($payload).'.'.$b64($signature);
}
$hash=hash('sha256',$secret);$db->beginTransaction();
try {
 $s=$db->prepare('SELECT secret_hash,device_hash,status FROM installations WHERE id=? FOR UPDATE');$s->execute([$id]);$row=$s->fetch(PDO::FETCH_ASSOC);
 if(!$row) { $s=$db->prepare("INSERT INTO installations(id,secret_hash,device_hash,status,created_at,last_seen,last_ip,app_version,os_version,cpu_model,ram_bytes,machine_name) VALUES(?,?,?,'pending',UTC_TIMESTAMP(),UTC_TIMESTAMP(),?,?,?,?,?,?)");$s->execute([$id,$hash,$device,ip(),$version,$os,$cpu,$ram,$machine]);$status='pending'; }
 else { if(!hash_equals($row['secret_hash'],$hash) || !hash_equals($row['device_hash'],$device)){$db->rollBack();reply(403,['error'=>'identity']);}
 $status=$row['status'];$s=$db->prepare('UPDATE installations SET last_seen=UTC_TIMESTAMP(),last_ip=?,app_version=?,os_version=?,cpu_model=?,ram_bytes=?,machine_name=? WHERE id=?');$s->execute([ip(),$version,$os,$cpu,$ram,$machine,$id]); }
 $s=$db->prepare('INSERT INTO execution_events(installation_id,observed_at,ip) VALUES(?,UTC_TIMESTAMP(),?)');$s->execute([$id,ip()]);
 $token=$status==='approved' ? issueToken($config,$device,$id) : null;$db->commit();
 reply(200,['status'=>$status,'token'=>$token]);
} catch(Throwable $e) {if($db->inTransaction())$db->rollBack();error_log('Jacquard telemetry transaction failed');reply(503,['error'=>'unavailable']);}
