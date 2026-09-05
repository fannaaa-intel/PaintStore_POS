-- =============================================================================
--  PaintStore POS — optional schema additions
--
--  Each statement is safe to run on an existing database and is NOT required:
--  the application detects whether the column is present and falls back to its
--  previous behaviour when it is not. Run them to enable the feature described.
--
--  MySQL / MariaDB does not support "ADD COLUMN IF NOT EXISTS" portably, so
--  re-running a statement that has already been applied reports
--  "Duplicate column name" — that error is harmless and means it is in place.
-- =============================================================================


-- -----------------------------------------------------------------------------
--  1. products.image_path                       [ALREADY APPLIED in the
--                                                 recovered dump — listed for
--                                                 fresh installs only]
--     Product photos on the Item List and in the POS catalog.
--     Referenced by InventoryItemListPanelBuilder / TransactionPanelBuilder.
-- -----------------------------------------------------------------------------
ALTER TABLE products
    ADD COLUMN image_path VARCHAR(255) DEFAULT NULL AFTER color_hex;


-- -----------------------------------------------------------------------------
--  2. purchase_orders.paid_at                   [NOT yet applied]
--     Lets the Accountant > Payables screen tell "received but unpaid" from
--     "received and paid".
--
--     Receiving goods is what creates the liability (the ledger credits
--     Accounts Payable at that moment). Without this column the screen cannot
--     express settlement, so it leaves received orders out entirely rather than
--     showing them as owed forever.
--
--     With it, received-but-unpaid orders appear as the real payables they are
--     and can be settled from the row detail.
-- -----------------------------------------------------------------------------
ALTER TABLE purchase_orders
    ADD COLUMN paid_at DATETIME DEFAULT NULL AFTER received_at;
