-- ============================================================
--  DarkRP S&Box — Schéma MySQL
--  Importez ce fichier UNE SEULE FOIS dans votre base de données.
--  Base : s119661_darkrpPietro
--  Exécutez via phpMyAdmin ou :  mysql -u USER -p DBNAME < darkrp_schema.sql
-- ============================================================

SET NAMES utf8mb4;
SET time_zone = '+00:00';

-- ── Table : players ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `players` (
    `steam_id`             BIGINT        NOT NULL,
    `steam_name`           VARCHAR(255)  NOT NULL DEFAULT '',
    `rp_name`              VARCHAR(255)  NULL,
    `last_ip`              VARCHAR(45)   NULL,

    -- Économie
    `money`                INT           NOT NULL DEFAULT 2500,
    `is_vip`               TINYINT(1)    NOT NULL DEFAULT 0,

    -- Staff
    `staff_role`           TINYINT       NOT NULL DEFAULT 0,

    -- Modération
    `warnings`             INT           NOT NULL DEFAULT 0,
    `is_jailed`            TINYINT(1)    NOT NULL DEFAULT 0,
    `jail_until`           DATETIME      NULL,

    -- Statistiques
    `playtime_seconds`     BIGINT        NOT NULL DEFAULT 0,
    `kills`                INT           NOT NULL DEFAULT 0,
    `deaths`               INT           NOT NULL DEFAULT 0,

    -- Dates
    `first_seen`           DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `last_seen`            DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Job / inventaire
    `last_job`             VARCHAR(500)  NULL,
    `persistent_inventory` JSON          NOT NULL DEFAULT (JSON_ARRAY()),

    PRIMARY KEY (`steam_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ── Table : bans ─────────────────────────────────────────────
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


-- ── Table : admin_logs ───────────────────────────────────────
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
    INDEX `idx_admin_steam_id`  (`admin_steam_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
