<?php
// Copy outside the public web root, configure permissions 0600, and set JACQUARD_CONFIG to its path.
return [
    'db_dsn' => 'mysql:host=localhost;dbname=jacquard;charset=utf8mb4',
    'db_user' => 'CHANGE_ME',
    'db_password' => 'CHANGE_ME',
    // Generate using: php -r 'echo password_hash(readline("Password: "), PASSWORD_DEFAULT), PHP_EOL;'
    'admin_hash' => '',
    'session_name' => 'jacquard_admin',
    'base_path' => '/app',
    // HTTPS is mandatory; set this true only for isolated local testing.
    'allow_local_http' => false,
];
