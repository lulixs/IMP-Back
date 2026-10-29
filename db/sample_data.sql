-- =============================================================
-- IMP sample data (for local testing only)
-- Run AFTER schema.sql. Safe to rerun: it clears every table first.
--
-- Test logins (employee PINs are 6 digits, stored as bcrypt hashes):
--   Daniel Finkle  PIN 111111
--   Thomas Anos    PIN 222222
--   Maria Lopez    PIN 333333
--   Grace Kim      PIN 444444
--   Ben Carter     PIN 555555  (deactivated, sign-in should fail)
--   Sam Ortiz      PIN 666666  (never signed in)
--   Master         master@example.com / master123
--
-- Dates are relative to today (CURDATE / NOW), so the alerts
-- page always has boxes older than the 180-day threshold.
-- =============================================================
SET NAMES utf8mb4;
USE IMP;

-- ---------- Clear old data ----------
SET FOREIGN_KEY_CHECKS = 0;
TRUNCATE TABLE AUDIT_LOG;
TRUNCATE TABLE BOX;
TRUNCATE TABLE DEVICE;
TRUNCATE TABLE CATEGORY;
TRUNCATE TABLE BUILDING;
TRUNCATE TABLE MASTER_ACCOUNT;
TRUNCATE TABLE EMPLOYEE_ACCOUNT;
SET FOREIGN_KEY_CHECKS = 1;

-- ---------- Employees ----------
-- pin_hash is the bcrypt hash of the 6-digit PIN shown in the comment at the end of each row.
INSERT INTO EMPLOYEE_ACCOUNT (id, name, pin_hash, is_activated, last_sign_in_at) VALUES
(1, 'Daniel Finkle', '$2b$10$zAe0rIkysQeIZRRpzPgrOew0ujDPYMrKMNxl8JVGF8ydxOmNFOcg2', TRUE,  NOW() - INTERVAL 20 MINUTE),  -- PIN 111111
(2, 'Thomas Anos',   '$2b$10$95OWXM4VED7.dw3ig2CIXux9KpSqu2A24RFkrQJqerGZIDxbs/6Sy', TRUE,  NOW() - INTERVAL 2 HOUR),  -- PIN 222222
(3, 'Maria Lopez',   '$2b$10$OAwFQQx2rFmrAqxhcHQz.uhDYdFmvm.yAg57OA7iPlcZtQJOmmsmi', TRUE,  NOW() - INTERVAL 1 DAY),  -- PIN 333333
(4, 'Grace Kim',     '$2b$10$jIzALHRcNg02zuCNkPwYY.4AfzjUOJOma2V4Bz9nXmjsasxrW34KW', TRUE,  NOW() - INTERVAL 15 DAY),  -- PIN 444444
(5, 'Ben Carter',    '$2b$10$5.VzIHTfiGouhHkDG6IjDObGWDhJWH5ZURdEa4P.I0UCEyEp4C1za', FALSE, NOW() - INTERVAL 40 DAY),  -- PIN 555555, DEACTIVATED, sign-in should fail
(6, 'Sam Ortiz',     '$2b$10$EnoRjctPDW0o/izfj3WurevYKhV.jcV28A1WAzP5oih.N7VjyoGWa', TRUE,  NULL);  -- PIN 666666, never signed in

-- ---------- Master account (only one row allowed) ----------
-- password_hash is the bcrypt hash of the master password: master123
INSERT INTO MASTER_ACCOUNT (id, email, password_hash, two_factor_secret, alert_threshold_days) VALUES
(1, 'master@example.com', '$2b$10$wWuAF9mkiBzflLMkUKaa8eBLKoYrMmEMY8dvC112vS./eJdluNmZq', NULL, 180);

-- ---------- Devices ----------
INSERT INTO DEVICE (id, device_name, active_employee_id, is_elevated, last_activity_at) VALUES
(1, 'Front desk tablet', 1,    FALSE, NOW() - INTERVAL 5 MINUTE),
(2, 'Stockroom tablet',  2,    TRUE,  NOW() - INTERVAL 30 MINUTE),
(3, 'Warehouse tablet',  NULL, FALSE, NOW() - INTERVAL 3 DAY);

-- ---------- Buildings ----------
-- "Garage Annex" has no boxes, so it can be used to test removing a building.
-- Building 5 ("Old Shed") was permanently deleted, so it only appears in the audit log.
INSERT INTO BUILDING (id, name) VALUES
(1, 'Main Store Stockroom'),
(2, 'Back Warehouse'),
(3, 'Storage Trailer'),
(4, 'Garage Annex');

-- ---------- Categories ----------
-- "Fall Decor" has no boxes, so it can be used to test deleting a category.
-- Category 10 ("Mother's Day") was permanently deleted, so it only appears in the audit log.
INSERT INTO CATEGORY (id, name, symbol) VALUES
(1, 'Christmas',        '🎄'),
(2, 'Halloween',        '🎃'),
(3, 'Easter',           '🐣'),
(4, 'Summer',           '☀️'),
(5, 'Back to School',   '🎒'),
(6, 'Thanksgiving',     '🦃'),
(7, 'Winter Coats',     '🧥'),
(8, 'Valentine''s Day', '❤️'),
(9, 'Fall Decor',       NULL);

-- ---------- Boxes ----------
-- Covers: full / half / empty, all three buildings and shelf levels,
-- 1- to 3-letter shelves, boxes past the 180-day alert threshold,
-- unracked boxes (12, 13), soft-deleted boxes (19, 20), a box that was
-- deleted and restored (10), and a box unracked then re-racked (7).
-- Box 21 was permanently deleted, so it only appears in the audit log.
INSERT INTO BOX (id, category_id, building_id, shelf_letter, shelf_level, fullness, notes, is_racked, racked_since, created_at, created_by, last_updated_at, last_updated_by, deleted_at, deleted_by) VALUES
(1,  1, 1, 'A',   1, 'FULL',  'Ornaments and tree toppers',              TRUE,  CURDATE() - INTERVAL 320 DAY, NOW() - INTERVAL 320 DAY, 1, NULL, NULL, NULL, NULL),
(2,  1, 1, 'A',   2, 'FULL',  'String lights, tested and working',       TRUE,  CURDATE() - INTERVAL 318 DAY, NOW() - INTERVAL 318 DAY, 1, NOW() - INTERVAL 10 DAY, 2, NULL, NULL),
(3,  1, 2, 'B',   1, 'HALF',  'Stockings and tree skirts',               TRUE,  CURDATE() - INTERVAL 300 DAY, NOW() - INTERVAL 300 DAY, 2, NULL, NULL, NULL, NULL),
(4,  2, 1, 'C',   3, 'FULL',  'Costumes, kids sizes',                    TRUE,  CURDATE() - INTERVAL 360 DAY, NOW() - INTERVAL 360 DAY, 3, NULL, NULL, NULL, NULL),
(5,  2, 2, 'C',   1, 'HALF',  NULL,                                      TRUE,  CURDATE() - INTERVAL 30 DAY,  NOW() - INTERVAL 30 DAY,  1, NULL, NULL, NULL, NULL),
(6,  3, 1, 'D',   2, 'FULL',  'Baskets and plastic eggs',                TRUE,  CURDATE() - INTERVAL 190 DAY, NOW() - INTERVAL 190 DAY, 2, NULL, NULL, NULL, NULL),
(7,  3, 3, 'AB',  1, 'EMPTY', 'Emptied for spring, keep for reuse',      TRUE,  CURDATE() - INTERVAL 150 DAY, NOW() - INTERVAL 200 DAY, 3, NOW() - INTERVAL 150 DAY, 3, NULL, NULL),
(8,  4, 2, 'E',   1, 'FULL',  'Beach towels and pool toys',              TRUE,  CURDATE() - INTERVAL 60 DAY,  NOW() - INTERVAL 60 DAY,  4, NULL, NULL, NULL, NULL),
(9,  4, 2, 'E',   2, 'HALF',  'Sandals, mixed sizes',                    TRUE,  CURDATE() - INTERVAL 58 DAY,  NOW() - INTERVAL 58 DAY,  4, NOW() - INTERVAL 5 DAY, 1, NULL, NULL),
(10, 5, 1, 'F',   1, 'FULL',  'Backpacks and lunch boxes',               TRUE,  CURDATE() - INTERVAL 75 DAY,  NOW() - INTERVAL 75 DAY,  1, NOW() - INTERVAL 3 DAY, 2, NULL, NULL),
(11, 5, 3, 'ABC', 3, 'HALF',  'Binders and pencil cases',                TRUE,  CURDATE() - INTERVAL 70 DAY,  NOW() - INTERVAL 70 DAY,  2, NULL, NULL, NULL, NULL),
(12, 6, 1, 'G',   2, 'FULL',  'Taken to sales floor for Thanksgiving',   FALSE, CURDATE() - INTERVAL 340 DAY, NOW() - INTERVAL 340 DAY, 3, NOW() - INTERVAL 4 DAY, 1, NULL, NULL),
(13, 7, 2, 'H',   1, 'HALF',  'Adult coats, out on the floor',           FALSE, CURDATE() - INTERVAL 210 DAY, NOW() - INTERVAL 210 DAY, 2, NOW() - INTERVAL 2 DAY, 2, NULL, NULL),
(14, 7, 2, 'H',   2, 'FULL',  'Kids coats and snow pants',               TRUE,  CURDATE() - INTERVAL 205 DAY, NOW() - INTERVAL 205 DAY, 2, NULL, NULL, NULL, NULL),
(15, 7, 3, 'J',   1, 'FULL',  'Hats, gloves, scarves',                   TRUE,  CURDATE() - INTERVAL 15 DAY,  NOW() - INTERVAL 15 DAY,  4, NULL, NULL, NULL, NULL),
(16, 8, 1, 'K',   3, 'HALF',  'Cards and decorations',                   TRUE,  CURDATE() - INTERVAL 250 DAY, NOW() - INTERVAL 250 DAY, 1, NULL, NULL, NULL, NULL),
(17, 1, 3, 'L',   2, 'FULL',  'Nativity sets, fragile',                  TRUE,  CURDATE() - INTERVAL 2 DAY,   NOW() - INTERVAL 2 DAY,   3, NULL, NULL, NULL, NULL),
(18, 2, 1, 'M',   1, 'EMPTY', NULL,                                      TRUE,  CURDATE() - INTERVAL 1 DAY,   NOW() - INTERVAL 1 DAY,   1, NULL, NULL, NULL, NULL),
(19, 4, 3, 'N',   1, 'HALF',  'Water-damaged, flagged for disposal',     TRUE,  CURDATE() - INTERVAL 100 DAY, NOW() - INTERVAL 100 DAY, 4, NOW() - INTERVAL 7 DAY, 2, NOW() - INTERVAL 7 DAY, 2),
(20, 3, 2, 'P',   3, 'EMPTY', 'Duplicate label, deleted',                TRUE,  CURDATE() - INTERVAL 20 DAY,  NOW() - INTERVAL 20 DAY,  1, NOW() - INTERVAL 1 DAY, 1, NOW() - INTERVAL 1 DAY, 1);

-- Records that were permanently deleted (box 21, category 10, building 5) only appear here.
-- Bump the counters past them, because ids are never reused (ERD).
ALTER TABLE BOX AUTO_INCREMENT = 22;
ALTER TABLE CATEGORY AUTO_INCREMENT = 11;
ALTER TABLE BUILDING AUTO_INCREMENT = 6;

-- ---------- Audit log ----------
-- Oldest first. Every box, category, building, and employee has its ADDED entry, and every
-- allowed action and target pairing appears at least once. Master-only actions have is_elevated TRUE.
-- target_label is the name at the time, so category 7 was added as "Coats" and later renamed.
INSERT INTO AUDIT_LOG (timestamp, employee_id, is_elevated, device_id, action_type, target_type, target_id, target_label, details) VALUES
-- Store setup: the first account adds itself (D-118), then buildings and the other employees.
(NOW() - INTERVAL 400 DAY, 1, TRUE,  1, 'ADDED',       'EMPLOYEE', 1,  'Daniel Finkle',        NULL),
(NOW() - INTERVAL 400 DAY, 1, TRUE,  1, 'ADDED',       'BUILDING', 1,  'Main Store Stockroom', NULL),
(NOW() - INTERVAL 400 DAY, 1, TRUE,  1, 'ADDED',       'BUILDING', 2,  'Back Warehouse',       NULL),
(NOW() - INTERVAL 400 DAY, 1, TRUE,  1, 'ADDED',       'BUILDING', 3,  'Storage Trailer',      NULL),
(NOW() - INTERVAL 400 DAY, 1, TRUE,  1, 'ADDED',       'BUILDING', 5,  'Old Shed',             NULL),
(NOW() - INTERVAL 400 DAY, 1, TRUE,  1, 'ADDED',       'EMPLOYEE', 2,  'Thomas Anos',          NULL),
(NOW() - INTERVAL 400 DAY, 1, TRUE,  1, 'ADDED',       'EMPLOYEE', 3,  'Maria Lopez',          NULL),
(NOW() - INTERVAL 400 DAY, 1, TRUE,  1, 'ADDED',       'EMPLOYEE', 4,  'Grace Kim',            NULL),
(NOW() - INTERVAL 400 DAY, 1, TRUE,  1, 'ADDED',       'EMPLOYEE', 5,  'Ben Carter',           NULL),
(NOW() - INTERVAL 399 DAY, 1, FALSE, 1, 'ADDED',       'CATEGORY', 1,  'Christmas',            NULL),
(NOW() - INTERVAL 399 DAY, 1, FALSE, 1, 'ADDED',       'CATEGORY', 2,  'Halloween',            NULL),
(NOW() - INTERVAL 399 DAY, 1, FALSE, 1, 'ADDED',       'CATEGORY', 3,  'Easter',               NULL),
(NOW() - INTERVAL 399 DAY, 1, FALSE, 1, 'ADDED',       'CATEGORY', 4,  'Summer',               NULL),
(NOW() - INTERVAL 399 DAY, 1, FALSE, 1, 'ADDED',       'CATEGORY', 5,  'Back to School',       NULL),
(NOW() - INTERVAL 399 DAY, 1, FALSE, 1, 'ADDED',       'CATEGORY', 6,  'Thanksgiving',         NULL),
(NOW() - INTERVAL 399 DAY, 1, FALSE, 1, 'ADDED',       'CATEGORY', 7,  'Coats',                NULL),
(NOW() - INTERVAL 399 DAY, 1, FALSE, 1, 'ADDED',       'CATEGORY', 8,  'Valentine''s Day',     NULL),
(NOW() - INTERVAL 399 DAY, 1, FALSE, 1, 'ADDED',       'CATEGORY', 10, 'Mother''s Day',        NULL),
-- A year of boxes.
(NOW() - INTERVAL 360 DAY, 3, FALSE, 3, 'ADDED',       'BOX',      4,  '#4',                   NULL),
(NOW() - INTERVAL 340 DAY, 3, FALSE, 3, 'ADDED',       'BOX',      12, '#12',                  NULL),
(NOW() - INTERVAL 320 DAY, 1, FALSE, 1, 'ADDED',       'BOX',      1,  '#1',                   NULL),
(NOW() - INTERVAL 318 DAY, 1, FALSE, 1, 'ADDED',       'BOX',      2,  '#2',                   NULL),
(NOW() - INTERVAL 300 DAY, 2, FALSE, 2, 'ADDED',       'BOX',      3,  '#3',                   NULL),
(NOW() - INTERVAL 300 DAY, 3, FALSE, 3, 'ADDED',       'BOX',      21, '#21',                  NULL),
(NOW() - INTERVAL 250 DAY, 1, FALSE, 1, 'ADDED',       'BOX',      16, '#16',                  NULL),
(NOW() - INTERVAL 210 DAY, 2, FALSE, 2, 'ADDED',       'BOX',      13, '#13',                  NULL),
(NOW() - INTERVAL 205 DAY, 2, FALSE, 2, 'ADDED',       'BOX',      14, '#14',                  NULL),
(NOW() - INTERVAL 200 DAY, 3, FALSE, 3, 'ADDED',       'BOX',      7,  '#7',                   NULL),
(NOW() - INTERVAL 190 DAY, 2, FALSE, 2, 'ADDED',       'BOX',      6,  '#6',                   NULL),
(NOW() - INTERVAL 165 DAY, 3, FALSE, 3, 'UNRACKED',    'BOX',      7,  '#7',                   NULL),
(NOW() - INTERVAL 150 DAY, 3, FALSE, 3, 'RACKED',      'BOX',      7,  '#7',                   JSON_OBJECT('field', 'racked_since', 'from', CURDATE() - INTERVAL 200 DAY, 'to', CURDATE() - INTERVAL 150 DAY)),
(NOW() - INTERVAL 120 DAY, 3, FALSE, 3, 'DELETED',     'BOX',      21, '#21',                  NULL),
(NOW() - INTERVAL 120 DAY, 1, TRUE,  1, 'PERMANENTLY_DELETED', 'CATEGORY', 10, 'Mother''s Day', NULL),
(NOW() - INTERVAL 110 DAY, 2, TRUE,  2, 'PERMANENTLY_DELETED', 'BOX',      21, '#21',           NULL),
(NOW() - INTERVAL 100 DAY, 4, FALSE, 3, 'ADDED',       'BOX',      19, '#19',                  NULL),
(NOW() - INTERVAL 100 DAY, 2, FALSE, 2, 'EDITED',      'CATEGORY', 7,  'Winter Coats',         '{"field": "name", "from": "Coats", "to": "Winter Coats"}'),
(NOW() - INTERVAL 90 DAY,  1, TRUE,  1, 'PERMANENTLY_DELETED', 'BUILDING', 5, 'Old Shed',       NULL),
(NOW() - INTERVAL 75 DAY,  1, FALSE, 1, 'ADDED',       'BOX',      10, '#10',                  NULL),
(NOW() - INTERVAL 70 DAY,  2, FALSE, 2, 'ADDED',       'BOX',      11, '#11',                  NULL),
(NOW() - INTERVAL 60 DAY,  4, FALSE, 3, 'ADDED',       'BOX',      8,  '#8',                   NULL),
(NOW() - INTERVAL 58 DAY,  4, FALSE, 3, 'ADDED',       'BOX',      9,  '#9',                   NULL),
(NOW() - INTERVAL 50 DAY,  1, TRUE,  1, 'EDITED',      'EMPLOYEE', 2,  'Thomas Anos',          '{"field": "pin", "note": "PIN reset"}'),
-- The last six weeks.
(NOW() - INTERVAL 40 DAY,  2, TRUE,  2, 'DEACTIVATED', 'EMPLOYEE', 5,  'Ben Carter',           NULL),
(NOW() - INTERVAL 30 DAY,  1, FALSE, 1, 'ADDED',       'BOX',      5,  '#5',                   NULL),
(NOW() - INTERVAL 30 DAY,  1, TRUE,  1, 'ADDED',       'EMPLOYEE', 6,  'Sam Ortiz',            NULL),
(NOW() - INTERVAL 20 DAY,  1, FALSE, 1, 'ADDED',       'BOX',      20, '#20',                  NULL),
(NOW() - INTERVAL 20 DAY,  1, TRUE,  1, 'DEACTIVATED', 'EMPLOYEE', 6,  'Sam Ortiz',            NULL),
(NOW() - INTERVAL 15 DAY,  4, FALSE, 3, 'ADDED',       'BOX',      15, '#15',                  NULL),
(NOW() - INTERVAL 10 DAY,  2, FALSE, 2, 'EDITED',      'BOX',      2,  '#2',                   '{"field": "notes", "from": "String lights", "to": "String lights, tested and working"}'),
(NOW() - INTERVAL 10 DAY,  2, TRUE,  2, 'ADDED',       'BUILDING', 4,  'Garage Annex',         NULL),
(NOW() - INTERVAL 7 DAY,   2, FALSE, 2, 'DELETED',     'BOX',      19, '#19',                  NULL),
(NOW() - INTERVAL 5 DAY,   1, FALSE, 1, 'EDITED',      'BOX',      9,  '#9',                   '{"field": "fullness", "from": "FULL", "to": "HALF"}'),
(NOW() - INTERVAL 4 DAY,   1, FALSE, 1, 'UNRACKED',    'BOX',      12, '#12',                  NULL),
(NOW() - INTERVAL 4 DAY,   1, FALSE, 1, 'DELETED',     'BOX',      10, '#10',                  NULL),
(NOW() - INTERVAL 3 DAY,   2, TRUE,  2, 'RESTORED',    'BOX',      10, '#10',                  NULL),
(NOW() - INTERVAL 2 DAY,   3, FALSE, 3, 'ADDED',       'BOX',      17, '#17',                  NULL),
(NOW() - INTERVAL 2 DAY,   2, FALSE, 2, 'UNRACKED',    'BOX',      13, '#13',                  NULL),
(NOW() - INTERVAL 2 DAY,   2, TRUE,  2, 'ACTIVATED',   'EMPLOYEE', 6,  'Sam Ortiz',            NULL),
(NOW() - INTERVAL 1 DAY,   1, FALSE, 1, 'ADDED',       'BOX',      18, '#18',                  NULL),
(NOW() - INTERVAL 1 DAY,   1, FALSE, 1, 'DELETED',     'BOX',      20, '#20',                  NULL),
(NOW() - INTERVAL 3 HOUR,  2, TRUE,  2, 'ADDED',       'CATEGORY', 9,  'Fall Decor',           NULL),
(NOW() - INTERVAL 2 HOUR,  2, TRUE,  2, 'EDITED',      'MASTER',   1,  'Alert threshold',      '{"field": "alert_threshold_days", "from": 120, "to": 180}');
