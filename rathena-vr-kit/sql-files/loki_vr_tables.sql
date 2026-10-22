-- Loki VR Non-Destructive Database Extensions for rAthena
-- Safe to run on any existing rAthena database: uses CREATE TABLE IF NOT EXISTS
-- and never modifies or drops existing rAthena tables.

CREATE TABLE IF NOT EXISTS `loki_account_settings` (
  `account_id` INT UNSIGNED NOT NULL,
  `saved_at` BIGINT NOT NULL COMMENT 'ms since 1970 UTC: when the player changed them',
  `data` MEDIUMTEXT NOT NULL COMMENT 'Loki VR per-account settings and Bestiary/Codex journal JSON',
  `updated_at` DATETIME NOT NULL,
  PRIMARY KEY (`account_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `loki_horde_reward` (
  `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `account_id` INT UNSIGNED NOT NULL,
  `base_pct` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'tenth of a percent of NextBaseExp (1000 = 100%)',
  `job_pct` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'tenth of a percent of NextJobExp (1000 = 100%)',
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_account` (`account_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
