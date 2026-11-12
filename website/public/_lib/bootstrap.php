<?php
declare(strict_types=1);
ini_set('display_errors','0');
error_reporting(E_ALL);
header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: no-referrer');
header('X-Frame-Options: DENY');
header("Content-Security-Policy: default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'");
$configPath=getenv('JACQUARD_CONFIG') ?: dirname(__DIR__,3).'/jacquard-private/config.php';
if (!$configPath || !is_file($configPath)) { http_response_code(503); exit('Service not configured.'); }
$config=require $configPath;
if (!is_array($config) || empty($config['db_dsn']) || empty($config['db_user']) || empty($config['admin_hash'])) { http_response_code(503); exit('Service not configured.'); }
$isHttps=isset($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off';
$local=($config['allow_local_http'] ?? false) === true && in_array($_SERVER['REMOTE_ADDR'] ?? '', ['127.0.0.1','::1'],true);
if (!$isHttps && !$local) { http_response_code(403); exit('HTTPS required.'); }
try { $db=new PDO($config['db_dsn'],$config['db_user'],$config['db_password'] ?? '',[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_EMULATE_PREPARES=>false]); }
catch(Throwable $e) { error_log('Jacquard database connection failed'); http_response_code(503); exit('Service unavailable.'); }
function esc(?string $s): string { return htmlspecialchars($s ?? '',ENT_QUOTES | ENT_SUBSTITUTE,'UTF-8'); }
function ip(): string { $ip=$_SERVER['REMOTE_ADDR'] ?? '';return filter_var($ip,FILTER_VALIDATE_IP) ? $ip : '0.0.0.0'; }
// Database atomic fixed-window rate limiter, independent of client-controlled forwarded headers.
function limit(PDO $db,string $scope,int $max,int $seconds): bool {
 $stmt=$db->prepare("INSERT INTO request_limits(scope,window_start,hits) VALUES(?,UTC_TIMESTAMP(),1) ON DUPLICATE KEY UPDATE hits=IF(window_start < UTC_TIMESTAMP() - INTERVAL $seconds SECOND,1,hits+1),window_start=IF(window_start < UTC_TIMESTAMP() - INTERVAL $seconds SECOND,UTC_TIMESTAMP(),window_start)");
 $stmt->execute([$scope]);$stmt=$db->prepare('SELECT hits FROM request_limits WHERE scope=?');$stmt->execute([$scope]);return (int)$stmt->fetchColumn() <= $max;
}
function start_admin_session(array $config): void {
 session_name($config['session_name'] ?? 'jacquard_admin');session_set_cookie_params(['path'=>($config['base_path'] ?? '/app').'/','secure'=>true,'httponly'=>true,'samesite'=>'Strict']);session_start();
 if (!isset($_SESSION['csrf'])) $_SESSION['csrf']=bin2hex(random_bytes(32));
}
