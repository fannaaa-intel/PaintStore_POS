-- =============================================================================
--  PaintStore POS — optional schema additions
--
--  HOW TO RUN
--    phpMyAdmin : select the paint_store_pos database on the left FIRST,
--                 then open the SQL tab, paste this file, press Go.
--    Command line:
--                 mysql -u root paint_store_pos < migrations.sql
--
--  Safe to run more than once: "IF NOT EXISTS" skips a column that is already
--  there instead of failing.
--
--  Neither column is required. The application checks whether each is present
--  and falls back to its previous behaviour when it is not, so an un-migrated
--  database is never shown wrong figures.
-- =============================================================================


-- -----------------------------------------------------------------------------
--  1. products.image_path
--     Product photos on the Item List and in the POS catalog.
--     Used by InventoryItemListPanelBuilder / TransactionPanelBuilder.
--     (Usually already present.)
-- -----------------------------------------------------------------------------
ALTER TABLE products
    ADD COLUMN IF NOT EXISTS image_path VARCHAR(255) DEFAULT NULL AFTER color_hex;


-- -----------------------------------------------------------------------------
--  2. purchase_orders.paid_at
--     Lets Accountant > Payables tell "received but unpaid" from
--     "received and paid".
--
--     Receiving goods is what creates the liability — the ledger credits
--     Accounts Payable at that moment. Without this column the screen cannot
--     express settlement, so it leaves received orders out entirely rather
--     than showing them as owed forever.
--
--     With it, received-but-unpaid orders appear as the real payables they
--     are, and can be settled from the row detail on the Payables screen.
-- -----------------------------------------------------------------------------
ALTER TABLE purchase_orders
    ADD COLUMN IF NOT EXISTS paid_at DATETIME DEFAULT NULL AFTER received_at;


-- -----------------------------------------------------------------------------
--  Check the result — both columns should be listed.
-- -----------------------------------------------------------------------------
SHOW COLUMNS FROM products LIKE 'image_path';
SHOW COLUMNS FROM purchase_orders LIKE 'paid_at';
