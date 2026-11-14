-- Empties every table, so the API starts from first-time setup. For your computer only.
-- Run from the repo folder:  docker exec -i imp-mysql mysql -uimp -pimp IMP < db/clear_data.sql
-- These are the same lines as the top of sample_data.sql. If a table is added there, add it here too.
SET FOREIGN_KEY_CHECKS = 0;
TRUNCATE TABLE AUDIT_LOG;
TRUNCATE TABLE BOX;
TRUNCATE TABLE DEVICE;
TRUNCATE TABLE CATEGORY;
TRUNCATE TABLE BUILDING;
TRUNCATE TABLE MASTER_ACCOUNT;
TRUNCATE TABLE EMPLOYEE_ACCOUNT;
SET FOREIGN_KEY_CHECKS = 1;
