-- =============================================================================
--  PaintStore POS — optional schema additions
--
--  Safe to run repeatedly. Each column is added only if it is missing, so
--  already-applied statements are skipped instead of aborting the batch.
--
--  Neither column is required. The application checks whether each is present
--  and falls back to its previous behaviour when it is not, so an un-migrated
--  database is never shown wrong figures.
--
--  Run with:
--    mysql -u root paint_store_pos < migrations.sql
-- =============================================================================


-- -----------------------------------------------------------------------------
--  1. products.image_path
--     Product photos on the Item List and in the POS catalog.
--     Used by InventoryItemListPanelBuilder / TransactionPanelBuilder.
-- -----------------------------------------------------------------------------
SET @ddl = (
    SELECT IF(COUNT(*) > 0,
        'SELECT ''products.image_path already present — skipped'' AS note',
        'ALTER TABLE products ADD COLUMN image_path VARCHAR(255) DEFAULT NULL AFTER color_hex')
    FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name   = 'products'
      AND column_name  = 'image_path'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;


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
SET @ddl = (
    SELECT IF(COUNT(*) > 0,
        'SELECT ''purchase_orders.paid_at already present — skipped'' AS note',
        'ALTER TABLE purchase_orders ADD COLUMN paid_at DATETIME DEFAULT NULL AFTER received_at')
    FROM information_schema.columns
    WHERE table_schema = DATABASE()
      AND table_name   = 'purchase_orders'
      AND column_name  = 'paid_at'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;


-- -----------------------------------------------------------------------------
--  Result
-- -----------------------------------------------------------------------------
SELECT
    MAX(column_name = 'image_path') AS products_image_path,
    (SELECT MAX(column_name = 'paid_at')
       FROM information_schema.columns
      WHERE table_schema = DATABASE() AND table_name = 'purchase_orders') AS po_paid_at
FROM information_schema.columns
WHERE table_schema = DATABASE() AND table_name = 'products';
