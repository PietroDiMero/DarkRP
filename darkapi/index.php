<?php
declare(strict_types=1);

/**
 * DarkRP Sidecar API — Pont entre S&Box et MySQL
 *
 * Démarre avec : php -S 127.0.0.1:9000 /chemin/vers/darkapi/index.php
 *
 * Routes :
 *   GET  /ping
 *   GET  /players              → tous les joueurs
 *   GET  /players/{steamId}    → un joueur
 *   POST /players              → créer/mettre à jour un joueur
 *   PATCH /players/{id}/money  → { "money": 5000 }
 *   PATCH /players/{id}/vip    → { "is_vip": true }
 *   PATCH /players/{id}/role   → { "staff_role": 4 }
 *   PATCH /players/{id}/stats  → { "kills": X, "deaths": X, "playtime_seconds": X }
 *   PATCH /players/{id}/jail   → { "is_jailed": true, "jail_until": "2026-05-07T12:00:00" }
 *   GET  /bans                 → bans actifs
 *   POST /bans                 → créer un ban
 *   PATCH /bans/{steamId}/unban
 *   GET  /logs?limit=50        → logs récents
 *   POST /logs                 → ajouter un log
 */

require_once __DIR__ . '/config.php';

// ── Utilitaires ───────────────────────────────────────────────────────────────

function json_out(mixed $data, int $status = 200): never
{
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

function json_in(): array
{
    $raw = file_get_contents('php://input');
    if (empty($raw)) return [];
    $data = json_decode($raw, true);
    if ($data === null) json_out(['error' => 'JSON invalide'], 400);
    return $data;
}

function db(): PDO
{
    static $pdo = null;
    if ($pdo !== null) return $pdo;

    $dsn = sprintf(
        'mysql:host=%s;port=%d;dbname=%s;charset=utf8mb4',
        DB_HOST, DB_PORT, DB_NAME
    );

    try {
        $pdo = new PDO($dsn, DB_USER, DB_PASS, [
            PDO::ATTR_ERRMODE            => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES   => false,
        ]);
    } catch (PDOException $e) {
        error_log('[DarkAPI] Connexion DB échouée : ' . $e->getMessage());
        json_out(['error' => 'Connexion base de données impossible'], 503);
    }

    return $pdo;
}

// ── Authentification ─────────────────────────────────────────────────────────
$apiKey = $_SERVER['HTTP_X_API_KEY'] ?? '';
if ($apiKey !== API_KEY) {
    json_out(['error' => 'Unauthorized'], 401);
}

// ── Parsing de la route ──────────────────────────────────────────────────────
$method = $_SERVER['REQUEST_METHOD'];
$uri    = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH);
$uri    = '/' . trim((string)$uri, '/');
$parts  = array_values(array_filter(explode('/', trim($uri, '/'))));
$p0     = $parts[0] ?? '';
$p1     = $parts[1] ?? '';
$p2     = $parts[2] ?? '';

// ── Dispatch ─────────────────────────────────────────────────────────────────

// /ping
if ($method === 'GET' && $uri === '/ping') {
    json_out(['status' => 'ok', 'time' => date('c'), 'version' => '1.0']);
}

// /players
if ($p0 === 'players') {
    if ($method === 'GET' && $p1 === '')    route_listPlayers();
    if ($method === 'GET' && $p1 !== '')    route_getPlayer((int)$p1);
    if ($method === 'POST' && $p1 === '')   route_upsertPlayer();
    if ($method === 'PATCH' && $p1 !== '' && $p2 !== '') route_patchPlayer((int)$p1, $p2);
}

// /bans
if ($p0 === 'bans') {
    if ($method === 'GET' && $p1 === '')    route_listBans();
    if ($method === 'POST' && $p1 === '')   route_createBan();
    if ($method === 'PATCH' && $p2 === 'unban') route_unbanPlayer((int)$p1);
}

// /logs
if ($p0 === 'logs') {
    if ($method === 'GET')  route_listLogs();
    if ($method === 'POST') route_createLog();
}

json_out(['error' => 'Route introuvable', 'uri' => $uri, 'method' => $method], 404);

// ═════════════════════════════════════════════════════════════════════════════
// PLAYERS
// ═════════════════════════════════════════════════════════════════════════════

function route_listPlayers(): never
{
    $rows = db()->query(
        'SELECT * FROM players ORDER BY last_seen DESC'
    )->fetchAll();

    json_out(array_map('normalize_player', $rows));
}

function route_getPlayer(int $steamId): never
{
    $stmt = db()->prepare('SELECT * FROM players WHERE steam_id = ?');
    $stmt->execute([$steamId]);
    $row = $stmt->fetch();

    if (!$row) json_out(['error' => 'Joueur introuvable'], 404);

    json_out(normalize_player($row));
}

function route_upsertPlayer(): never
{
    $d = json_in();

    if (empty($d['steam_id'])) {
        json_out(['error' => 'steam_id requis'], 400);
    }

    // Inventaire persistant : on s'assure que c'est bien du JSON
    $inventory = $d['persistent_inventory'] ?? [];
    if (!is_array($inventory)) $inventory = [];

    // Dates : conversion depuis ISO 8601 vers MySQL DATETIME
    $firstSeen = iso_to_mysql($d['first_seen'] ?? null) ?? date('Y-m-d H:i:s');
    $lastSeen  = iso_to_mysql($d['last_seen']  ?? null) ?? date('Y-m-d H:i:s');
    $jailUntil = iso_to_mysql($d['jail_until'] ?? null);

    $stmt = db()->prepare('
        INSERT INTO players
            (steam_id, steam_name, rp_name, last_ip, money, is_vip, staff_role,
             warnings, is_jailed, jail_until, playtime_seconds, kills, deaths,
             first_seen, last_seen, last_job, persistent_inventory)
        VALUES
            (:steam_id, :steam_name, :rp_name, :last_ip, :money, :is_vip, :staff_role,
             :warnings, :is_jailed, :jail_until, :playtime_seconds, :kills, :deaths,
             :first_seen, :last_seen, :last_job, :persistent_inventory)
        ON DUPLICATE KEY UPDATE
            steam_name           = VALUES(steam_name),
            rp_name              = COALESCE(VALUES(rp_name), rp_name),
            last_ip              = VALUES(last_ip),
            money                = VALUES(money),
            is_vip               = VALUES(is_vip),
            staff_role           = VALUES(staff_role),
            warnings             = VALUES(warnings),
            is_jailed            = VALUES(is_jailed),
            jail_until           = VALUES(jail_until),
            playtime_seconds     = VALUES(playtime_seconds),
            kills                = VALUES(kills),
            deaths               = VALUES(deaths),
            last_seen            = VALUES(last_seen),
            last_job             = COALESCE(VALUES(last_job), last_job),
            persistent_inventory = VALUES(persistent_inventory)
    ');

    $stmt->execute([
        ':steam_id'             => (int)$d['steam_id'],
        ':steam_name'           => $d['steam_name'] ?? '',
        ':rp_name'              => $d['rp_name'] ?? null,
        ':last_ip'              => $d['last_ip'] ?? null,
        ':money'                => (int)($d['money'] ?? 2500),
        ':is_vip'               => (int)(bool)($d['is_vip'] ?? false),
        ':staff_role'           => (int)($d['staff_role'] ?? 0),
        ':warnings'             => (int)($d['warnings'] ?? 0),
        ':is_jailed'            => (int)(bool)($d['is_jailed'] ?? false),
        ':jail_until'           => $jailUntil,
        ':playtime_seconds'     => (int)($d['playtime_seconds'] ?? 0),
        ':kills'                => (int)($d['kills'] ?? 0),
        ':deaths'               => (int)($d['deaths'] ?? 0),
        ':first_seen'           => $firstSeen,
        ':last_seen'            => $lastSeen,
        ':last_job'             => $d['last_job'] ?? null,
        ':persistent_inventory' => json_encode($inventory),
    ]);

    json_out(['status' => 'ok']);
}

function route_patchPlayer(int $steamId, string $field): never
{
    $d = json_in();

    switch ($field) {

        case 'money':
            $stmt = db()->prepare('UPDATE players SET money = ? WHERE steam_id = ?');
            $stmt->execute([(int)($d['money'] ?? 0), $steamId]);
            break;

        case 'vip':
            $stmt = db()->prepare('UPDATE players SET is_vip = ? WHERE steam_id = ?');
            $stmt->execute([(int)(bool)($d['is_vip'] ?? false), $steamId]);
            break;

        case 'role':
            $stmt = db()->prepare('UPDATE players SET staff_role = ? WHERE steam_id = ?');
            $stmt->execute([(int)($d['staff_role'] ?? 0), $steamId]);
            break;

        case 'stats':
            $stmt = db()->prepare(
                'UPDATE players SET kills = ?, deaths = ?, playtime_seconds = ? WHERE steam_id = ?'
            );
            $stmt->execute([
                (int)($d['kills'] ?? 0),
                (int)($d['deaths'] ?? 0),
                (int)($d['playtime_seconds'] ?? 0),
                $steamId,
            ]);
            break;

        case 'playtime':
            $stmt = db()->prepare('UPDATE players SET playtime_seconds = ? WHERE steam_id = ?');
            $stmt->execute([(int)($d['playtime_seconds'] ?? 0), $steamId]);
            break;

        case 'warnings':
            $stmt = db()->prepare('UPDATE players SET warnings = ? WHERE steam_id = ?');
            $stmt->execute([(int)($d['warnings'] ?? 0), $steamId]);
            break;

        case 'jail':
            $jailUntil = iso_to_mysql($d['jail_until'] ?? null);
            $stmt = db()->prepare(
                'UPDATE players SET is_jailed = ?, jail_until = ? WHERE steam_id = ?'
            );
            $stmt->execute([
                (int)(bool)($d['is_jailed'] ?? false),
                $jailUntil,
                $steamId,
            ]);
            break;

        default:
            json_out(['error' => "Champ '$field' inconnu"], 400);
    }

    json_out(['status' => 'ok', 'affected' => db()->query('SELECT ROW_COUNT()')->fetchColumn()]);
}

// ── Normalisation d'une ligne players → tableau PHP propre ────────────────────
function normalize_player(array $row): array
{
    $row['steam_id']            = (int)$row['steam_id'];
    $row['money']               = (int)$row['money'];
    $row['is_vip']              = (bool)$row['is_vip'];
    $row['staff_role']          = (int)$row['staff_role'];
    $row['warnings']            = (int)$row['warnings'];
    $row['is_jailed']           = (bool)$row['is_jailed'];
    $row['playtime_seconds']    = (int)$row['playtime_seconds'];
    $row['kills']               = (int)$row['kills'];
    $row['deaths']              = (int)$row['deaths'];
    $row['persistent_inventory'] = json_decode($row['persistent_inventory'] ?? '[]', true) ?? [];

    // Convertir les dates MySQL vers ISO 8601 pour C# (DateTime.Parse)
    foreach (['first_seen', 'last_seen', 'jail_until'] as $col) {
        if (!empty($row[$col])) {
            $row[$col] = (new DateTimeImmutable($row[$col], new DateTimeZone('UTC')))->format('c');
        }
    }

    return $row;
}

// ═════════════════════════════════════════════════════════════════════════════
// BANS
// ═════════════════════════════════════════════════════════════════════════════

function route_listBans(): never
{
    // Retourne tous les bans actifs (is_active = 1)
    // NOTE : on n'expose pas la colonne `id` (int MySQL) car BanRecord.Id est un Guid en C#
    $rows = db()->query(
        'SELECT steam_id, ip, display_name, reason, admin_steam_id, admin_name,
                banned_at AS created_at, expires_at, is_active
         FROM bans
         WHERE is_active = 1
         ORDER BY banned_at DESC'
    )->fetchAll();

    json_out(array_map('normalize_ban', $rows));
}

function route_createBan(): never
{
    $d = json_in();

    if (empty($d['steam_id']) && empty($d['ip'])) {
        json_out(['error' => 'steam_id ou ip requis'], 400);
    }

    $expiresAt = iso_to_mysql($d['expires_at'] ?? null);

    $stmt = db()->prepare('
        INSERT INTO bans (steam_id, ip, display_name, reason, admin_steam_id, admin_name, expires_at, is_active)
        VALUES (:steam_id, :ip, :display_name, :reason, :admin_steam_id, :admin_name, :expires_at, 1)
    ');

    $stmt->execute([
        ':steam_id'       => isset($d['steam_id']) ? (int)$d['steam_id'] : null,
        ':ip'             => $d['ip'] ?? null,
        ':display_name'   => $d['display_name'] ?? 'Inconnu',
        ':reason'         => $d['reason'] ?? 'Banni',
        ':admin_steam_id' => isset($d['admin_steam_id']) ? (int)$d['admin_steam_id'] : null,
        ':admin_name'     => $d['admin_name'] ?? null,
        ':expires_at'     => $expiresAt,
    ]);

    json_out(['status' => 'ok', 'id' => (int)db()->lastInsertId()]);
}

function route_unbanPlayer(int $steamId): never
{
    $stmt = db()->prepare(
        'UPDATE bans SET is_active = 0 WHERE steam_id = ? AND is_active = 1'
    );
    $stmt->execute([$steamId]);

    json_out(['status' => 'ok', 'affected' => $stmt->rowCount()]);
}

function normalize_ban(array $row): array
{
    $row['steam_id']        = $row['steam_id'] !== null ? (int)$row['steam_id'] : null;
    $row['admin_steam_id']  = $row['admin_steam_id'] !== null ? (int)$row['admin_steam_id'] : null;
    $row['is_active']       = (bool)$row['is_active'];

    // Dates → ISO 8601
    foreach (['created_at', 'expires_at'] as $col) {
        if (!empty($row[$col])) {
            $row[$col] = (new DateTimeImmutable($row[$col], new DateTimeZone('UTC')))->format('c');
        }
    }

    return $row;
}

// ═════════════════════════════════════════════════════════════════════════════
// LOGS
// ═════════════════════════════════════════════════════════════════════════════

function route_listLogs(): never
{
    $limit = min((int)($_GET['limit'] ?? 50), 500);

    // NOTE : on n'expose pas la colonne `id` (int MySQL) car AdminLog.Id est un Guid en C#
    $stmt = db()->prepare(
        'SELECT admin_steam_id, admin_name, target_steam_id, target_name,
                action, details, timestamp AS created_at
         FROM admin_logs
         ORDER BY timestamp DESC
         LIMIT ?'
    );
    $stmt->execute([$limit]);
    $rows = $stmt->fetchAll();

    json_out(array_map('normalize_log', $rows));
}

function route_createLog(): never
{
    $d = json_in();

    $stmt = db()->prepare('
        INSERT INTO admin_logs (admin_steam_id, admin_name, target_steam_id, target_name, action, details)
        VALUES (:admin_steam_id, :admin_name, :target_steam_id, :target_name, :action, :details)
    ');

    $stmt->execute([
        ':admin_steam_id'  => (int)($d['admin_steam_id'] ?? 0),
        ':admin_name'      => $d['admin_name'] ?? 'Serveur',
        ':target_steam_id' => isset($d['target_steam_id']) ? (int)$d['target_steam_id'] : null,
        ':target_name'     => $d['target_name'] ?? null,
        ':action'          => $d['action'] ?? '',
        ':details'         => $d['details'] ?? '',
    ]);

    json_out(['status' => 'ok']);
}

function normalize_log(array $row): array
{
    $row['admin_steam_id']  = (int)$row['admin_steam_id'];
    $row['target_steam_id'] = $row['target_steam_id'] !== null ? (int)$row['target_steam_id'] : null;

    if (!empty($row['created_at'])) {
        $row['created_at'] = (new DateTimeImmutable($row['created_at'], new DateTimeZone('UTC')))->format('c');
    }

    return $row;
}

// ═════════════════════════════════════════════════════════════════════════════
// HELPERS
// ═════════════════════════════════════════════════════════════════════════════

/**
 * Convertit une date ISO 8601 (depuis C#) en format MySQL DATETIME.
 * Retourne null si la valeur est vide ou invalide.
 */
function iso_to_mysql(?string $iso): ?string
{
    if (empty($iso)) return null;
    try {
        return (new DateTimeImmutable($iso))->setTimezone(new DateTimeZone('UTC'))->format('Y-m-d H:i:s');
    } catch (Throwable) {
        return null;
    }
}
