-- ============================================================
--  DarkRP S&Box — Schéma MySQL v2
--  Base : u177740262_darkrp (Hostinger)
--  Importez via phpMyAdmin ou : mysql -u USER -p DBNAME < darkrp_schema_v2.sql
--
--  ⚠️  Si vous migrez depuis darkrp_schema.sql (v1), les 3 premières tables
--      (players, bans, admin_logs) existent déjà : les CREATE TABLE IF NOT EXISTS
--      sont safe, ils ne toucheront pas vos données.
-- ============================================================

SET NAMES utf8mb4;
SET time_zone = '+00:00';

-- ╔══════════════════════════════════════════════════════════╗
-- ║  TABLES EXISTANTES (v1) — inchangées                      ║
-- ╚══════════════════════════════════════════════════════════╝

-- ── Table : players ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `players` (
    `steam_id`             BIGINT        NOT NULL,
    `steam_name`           VARCHAR(255)  NOT NULL DEFAULT '',
    `rp_name`              VARCHAR(255)  NULL,
    `last_ip`              VARCHAR(45)   NULL,
    `money`                INT           NOT NULL DEFAULT 2500,
    `is_vip`               TINYINT(1)    NOT NULL DEFAULT 0,
    `staff_role`           TINYINT       NOT NULL DEFAULT 0,
    `warnings`             INT           NOT NULL DEFAULT 0,
    `is_jailed`            TINYINT(1)    NOT NULL DEFAULT 0,
    `jail_until`           DATETIME      NULL,
    `playtime_seconds`     BIGINT        NOT NULL DEFAULT 0,
    `kills`                INT           NOT NULL DEFAULT 0,
    `deaths`               INT           NOT NULL DEFAULT 0,
    `first_seen`           DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `last_seen`            DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `last_job`             VARCHAR(500)  NULL,
    `persistent_inventory` JSON          NOT NULL DEFAULT (JSON_ARRAY()),
    PRIMARY KEY (`steam_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── Table : bans ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `bans` (
    `id`              INT           NOT NULL AUTO_INCREMENT,
    `steam_id`        BIGINT        NULL,
    `ip`              VARCHAR(45)   NULL,
    `display_name`    VARCHAR(255)  NOT NULL DEFAULT 'Inconnu',
    `reason`          VARCHAR(500)  NOT NULL DEFAULT 'Banni',
    `admin_steam_id`  BIGINT        NULL,
    `admin_name`      VARCHAR(255)  NULL,
    `banned_at`       DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `expires_at`      DATETIME      NULL,
    `is_active`       TINYINT(1)    NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    INDEX `idx_steam_id`  (`steam_id`),
    INDEX `idx_ip`        (`ip`),
    INDEX `idx_is_active` (`is_active`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── Table : admin_logs ──────────────────────────────────────
-- Trace les actions in-game (incluant les kicks via action='kick')
CREATE TABLE IF NOT EXISTS `admin_logs` (
    `id`               INT          NOT NULL AUTO_INCREMENT,
    `admin_steam_id`   BIGINT       NOT NULL DEFAULT 0,
    `admin_name`       VARCHAR(255) NOT NULL DEFAULT 'Serveur',
    `target_steam_id`  BIGINT       NULL,
    `target_name`      VARCHAR(255) NULL,
    `action`           VARCHAR(100) NOT NULL DEFAULT '',
    `details`          TEXT         NULL,
    `timestamp`        DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    INDEX `idx_timestamp`       (`timestamp`),
    INDEX `idx_target_steam_id` (`target_steam_id`),
    INDEX `idx_admin_steam_id`  (`admin_steam_id`),
    INDEX `idx_action`          (`action`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ╔══════════════════════════════════════════════════════════╗
-- ║  NOUVELLES TABLES v2 — Panel admin                        ║
-- ╚══════════════════════════════════════════════════════════╝

-- ── admin_users : extension de players pour les sessions panel ─
-- Le rôle est dérivé dynamiquement de players.staff_role à chaque login Steam.
-- Pas de password_hash : auth via Steam OpenID uniquement.
CREATE TABLE IF NOT EXISTS `admin_users` (
    `steam_id`         BIGINT       NOT NULL,
    `last_login`       DATETIME     NULL,
    `last_ip`          VARCHAR(45)  NULL,
    `panel_2fa_secret` VARCHAR(255) NULL,                       -- optionnel (V2)
    `notes`            TEXT         NULL,
    `created_at`       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`steam_id`),
    CONSTRAINT `fk_admin_users_player`
        FOREIGN KEY (`steam_id`) REFERENCES `players`(`steam_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── panel_audit_log : actions effectuées DANS le panel web ────
-- Distinct d'admin_logs (qui trace les actions in-game).
CREATE TABLE IF NOT EXISTS `panel_audit_log` (
    `id`             INT           NOT NULL AUTO_INCREMENT,
    `admin_steam_id` BIGINT        NOT NULL,
    `action`         VARCHAR(100)  NOT NULL,                    -- 'ban.create', 'player.money', 'login'…
    `target_type`    VARCHAR(50)   NULL,                        -- 'player' / 'ban' / 'ticket'
    `target_id`      VARCHAR(100)  NULL,
    `payload`        JSON          NULL,                        -- valeurs avant/après
    `ip`             VARCHAR(45)   NULL,
    `created_at`     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    INDEX `idx_admin_steam_id` (`admin_steam_id`),
    INDEX `idx_created_at`     (`created_at`),
    INDEX `idx_action`         (`action`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ╔══════════════════════════════════════════════════════════╗
-- ║  MODÉRATION                                                ║
-- ╚══════════════════════════════════════════════════════════╝

-- ── warnings : warns détaillés (players.warnings reste comme compteur dérivé) ─
CREATE TABLE IF NOT EXISTS `warnings` (
    `id`             INT           NOT NULL AUTO_INCREMENT,
    `steam_id`       BIGINT        NOT NULL,
    `reason`         VARCHAR(500)  NOT NULL,
    `admin_steam_id` BIGINT        NOT NULL,
    `admin_name`     VARCHAR(255)  NOT NULL,
    `is_active`      TINYINT(1)    NOT NULL DEFAULT 1,          -- soft delete (retrait de warn)
    `created_at`     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    INDEX `idx_steam_id`  (`steam_id`),
    INDEX `idx_is_active` (`is_active`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── tickets : support (un joueur ouvre, le staff répond) ──────
CREATE TABLE IF NOT EXISTS `tickets` (
    `id`          INT          NOT NULL AUTO_INCREMENT,
    `steam_id`    BIGINT       NOT NULL,
    `subject`     VARCHAR(255) NOT NULL,
    `category`    ENUM('bug','question','signalement','autre') NOT NULL DEFAULT 'question',
    `status`      ENUM('open','in_progress','waiting','closed') NOT NULL DEFAULT 'open',
    `priority`    ENUM('low','normal','high','urgent') NOT NULL DEFAULT 'normal',
    `assigned_to` BIGINT       NULL,                            -- steam_id admin assigné
    `created_at`  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `updated_at`  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    `closed_at`   DATETIME     NULL,
    PRIMARY KEY (`id`),
    INDEX `idx_status`   (`status`),
    INDEX `idx_steam_id` (`steam_id`),
    INDEX `idx_assigned` (`assigned_to`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── ticket_messages : fil de discussion d'un ticket ───────────
CREATE TABLE IF NOT EXISTS `ticket_messages` (
    `id`              INT          NOT NULL AUTO_INCREMENT,
    `ticket_id`       INT          NOT NULL,
    `author_steam_id` BIGINT       NOT NULL,
    `author_name`     VARCHAR(255) NOT NULL,
    `is_staff`        TINYINT(1)   NOT NULL DEFAULT 0,
    `body`            TEXT         NOT NULL,
    `created_at`      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    INDEX `idx_ticket_id` (`ticket_id`),
    CONSTRAINT `fk_ticket_messages_ticket`
        FOREIGN KEY (`ticket_id`) REFERENCES `tickets`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── reports : signalements joueur → joueur ────────────────────
CREATE TABLE IF NOT EXISTS `reports` (
    `id`                INT           NOT NULL AUTO_INCREMENT,
    `reporter_steam_id` BIGINT        NOT NULL,
    `target_steam_id`   BIGINT        NOT NULL,
    `reason`            VARCHAR(500)  NOT NULL,
    `evidence_url`      VARCHAR(1000) NULL,                     -- screenshot / vidéo
    `status`            ENUM('pending','reviewing','resolved','dismissed') NOT NULL DEFAULT 'pending',
    `handled_by`        BIGINT        NULL,                     -- steam_id admin
    `resolution_note`   TEXT          NULL,
    `created_at`        DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `resolved_at`       DATETIME      NULL,
    PRIMARY KEY (`id`),
    INDEX `idx_status` (`status`),
    INDEX `idx_target` (`target_steam_id`),
    INDEX `idx_reporter` (`reporter_steam_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ╔══════════════════════════════════════════════════════════╗
-- ║  SESSIONS / SÉCURITÉ                                       ║
-- ╚══════════════════════════════════════════════════════════╝

-- ── player_sessions : historique connexions (IP + HWID + géoloc) ─
CREATE TABLE IF NOT EXISTS `player_sessions` (
    `id`               INT           NOT NULL AUTO_INCREMENT,
    `steam_id`         BIGINT        NOT NULL,
    `ip`               VARCHAR(45)   NOT NULL,
    `hwid`             VARCHAR(128)  NULL,                      -- fingerprint hardware (si S&Box l'expose)
    `country_code`     CHAR(2)       NULL,
    `country_name`     VARCHAR(100)  NULL,
    `city`             VARCHAR(100)  NULL,
    `asn`              VARCHAR(100)  NULL,                      -- détection VPN/proxy
    `is_vpn`           TINYINT(1)    NOT NULL DEFAULT 0,
    `connected_at`     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `disconnected_at`  DATETIME      NULL,
    `duration_seconds` INT           NULL,                      -- calculé à la déconnexion
    PRIMARY KEY (`id`),
    INDEX `idx_steam_id`     (`steam_id`),
    INDEX `idx_ip`           (`ip`),
    INDEX `idx_hwid`         (`hwid`),
    INDEX `idx_connected_at` (`connected_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── player_fingerprints : résumé des empreintes uniques d'un joueur ─
-- Utile pour "Quels autres joueurs ont utilisé cette IP/HWID ?"
CREATE TABLE IF NOT EXISTS `player_fingerprints` (
    `id`               INT           NOT NULL AUTO_INCREMENT,
    `steam_id`         BIGINT        NOT NULL,
    `fingerprint_type` ENUM('ip','hwid') NOT NULL,
    `value`            VARCHAR(128)  NOT NULL,
    `first_seen`       DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `last_seen`        DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `session_count`    INT           NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_steam_fp` (`steam_id`, `fingerprint_type`, `value`),
    INDEX `idx_value_lookup` (`fingerprint_type`, `value`)      -- recherche multi-comptes
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ╔══════════════════════════════════════════════════════════╗
-- ║  QUEUE D'ACTIONS PANEL → SERVEUR S&BOX                    ║
-- ╚══════════════════════════════════════════════════════════╝

-- ── pending_actions : actions en attente d'exécution par S&Box ─
-- S&Box poll cette table toutes les 2s, exécute, marque processed_at = NOW().
-- À supprimer si on bascule sur WebSocket en V2.
CREATE TABLE IF NOT EXISTS `pending_actions` (
    `id`              INT           NOT NULL AUTO_INCREMENT,
    `target_steam_id` BIGINT        NOT NULL,
    `action`          VARCHAR(50)   NOT NULL,                   -- 'kick', 'jail', 'unban', 'set_money', 'announce'…
    `payload`         JSON          NULL,
    `created_by`      BIGINT        NOT NULL,                   -- admin steam_id
    `created_at`      DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `processed_at`    DATETIME      NULL,                       -- NULL = pas encore exécuté
    `result`          VARCHAR(255)  NULL,                       -- 'ok' / message d'erreur
    PRIMARY KEY (`id`),
    INDEX `idx_unprocessed` (`processed_at`, `target_steam_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ╔══════════════════════════════════════════════════════════╗
-- ║  ÉCONOMIE / MÉTIERS                                        ║
-- ╚══════════════════════════════════════════════════════════╝

-- ── factions : organisations RP staff-définies (Police, Triade…) ─
CREATE TABLE IF NOT EXISTS `factions` (
    `id`           INT          NOT NULL AUTO_INCREMENT,
    `name`         VARCHAR(100) NOT NULL,
    `display_name` VARCHAR(100) NOT NULL,
    `description`  TEXT         NULL,
    `color_hex`    CHAR(7)      NULL,                           -- ex: '#FF0000'
    `icon_url`     VARCHAR(500) NULL,
    `is_active`    TINYINT(1)   NOT NULL DEFAULT 1,
    `created_at`   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── jobs : référentiel des métiers (source de vérité = code C#) ─
CREATE TABLE IF NOT EXISTS `jobs` (
    `id`             INT          NOT NULL AUTO_INCREMENT,
    `code`           VARCHAR(50)  NOT NULL,                     -- ex: 'ubay_eats', 'cop'
    `display_name`   VARCHAR(100) NOT NULL,
    `faction_id`     INT          NULL,
    `base_salary`    INT          NOT NULL DEFAULT 0,
    `is_whitelisted` TINYINT(1)   NOT NULL DEFAULT 0,           -- réservé staff/VIP
    `max_slots`      INT          NULL,                         -- nb max simultané
    `description`    TEXT         NULL,
    `is_active`      TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_code` (`code`),
    INDEX `idx_faction` (`faction_id`),
    CONSTRAINT `fk_jobs_faction`
        FOREIGN KEY (`faction_id`) REFERENCES `factions`(`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── player_jobs : métiers actuels d'un joueur (slot 1 / slot 2 cf. screen GTA RP) ─
CREATE TABLE IF NOT EXISTS `player_jobs` (
    `id`        INT      NOT NULL AUTO_INCREMENT,
    `steam_id`  BIGINT   NOT NULL,
    `job_id`    INT      NOT NULL,
    `slot`      TINYINT  NOT NULL DEFAULT 1,                    -- 1 ou 2
    `joined_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_player_slot` (`steam_id`, `slot`),
    INDEX `idx_job_id` (`job_id`),
    CONSTRAINT `fk_player_jobs_job`
        FOREIGN KEY (`job_id`) REFERENCES `jobs`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ── economy_transactions : historique des mouvements d'argent ─
-- Permet de détecter dupes/abus et de retracer un solde anormal.
CREATE TABLE IF NOT EXISTS `economy_transactions` (
    `id`                    INT          NOT NULL AUTO_INCREMENT,
    `steam_id`              BIGINT       NOT NULL,
    `amount`                INT          NOT NULL,              -- positif = gain, négatif = dépense
    `balance_after`         INT          NOT NULL,
    `source`                VARCHAR(100) NOT NULL,              -- 'salary', 'sale', 'admin_set', 'transfer', 'death_drop'…
    `counterparty_steam_id` BIGINT       NULL,                  -- transferts joueur↔joueur
    `note`                  VARCHAR(500) NULL,
    `created_at`            DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    INDEX `idx_steam_date` (`steam_id`, `created_at`),
    INDEX `idx_source`     (`source`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ╔══════════════════════════════════════════════════════════╗
-- ║  COMMUNICATION                                             ║
-- ╚══════════════════════════════════════════════════════════╝

-- ── server_announcements : annonces broadcast depuis le panel ─
CREATE TABLE IF NOT EXISTS `server_announcements` (
    `id`         INT          NOT NULL AUTO_INCREMENT,
    `title`      VARCHAR(255) NOT NULL,
    `body`       TEXT         NOT NULL,
    `type`       ENUM('info','warning','event','maintenance') NOT NULL DEFAULT 'info',
    `created_by` BIGINT       NOT NULL,                         -- admin steam_id
    `starts_at`  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `ends_at`    DATETIME     NULL,
    `is_active`  TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    INDEX `idx_active_window` (`is_active`, `starts_at`, `ends_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ============================================================
--  FIN DU SCHÉMA v2
--  Total : 17 tables (3 existantes + 14 nouvelles)
-- ============================================================
