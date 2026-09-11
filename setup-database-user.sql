-- =============================================================================
--  setup-database-user.sql
--
--  RUN THIS ONCE on a new machine, AFTER importing paint_store_pos_full.sql.
--
--  WHY IT IS NEEDED
--  The app does not connect to MySQL as root. It uses a limited account called
--  'pos_app' that can read and write rows but cannot DROP or ALTER anything, so
--  a bug or an injected statement cannot destroy the schema.
--
--  That account lives in MySQL's own user list, NOT inside the paint_store_pos
--  database -- so importing the database does not bring it along. Without this
--  step the app fails to start with:
--
--      Access denied for user 'pos_app'@'localhost'
--
--  BEFORE YOU RUN IT
--  Replace CHANGE_THIS_PASSWORD below (both places) with a password of your
--  choosing, then put the SAME password into App.config and
--  PaintStore_POS.exe.config:
--
--      uid=pos_app;pwd=THE_PASSWORD_YOU_CHOSE;
--
--  Tip: to reuse the password already working on the original machine, open its
--  App.config and copy the value after pwd=.
--
--  HOW TO RUN
--    phpMyAdmin : SQL tab, paste this whole file, press Go.
--    Command line:
--      C:\xampp\mysql\bin\mysql.exe -u root < setup-database-user.sql
-- =============================================================================

CREATE USER IF NOT EXISTS 'pos_app'@'localhost' IDENTIFIED BY 'CHANGE_THIS_PASSWORD';
ALTER USER 'pos_app'@'localhost' IDENTIFIED BY 'CHANGE_THIS_PASSWORD';

-- Only these four. The app never needs to create or drop anything at runtime;
-- schema changes are applied by hand from migrations.sql.
GRANT SELECT, INSERT, UPDATE, DELETE ON paint_store_pos.* TO 'pos_app'@'localhost';

FLUSH PRIVILEGES;

-- Should list one row. Empty means the account was not created.
SELECT user, host FROM mysql.user WHERE user = 'pos_app';
