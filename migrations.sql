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
--  3. suppliers.image_path
--     Supplier logo / profile photo, shown on the Procurement > Suppliers list,
--     the supplier detail header, and the add/edit form.
--     Used by ProcurementSuppliersPanelBuilder.
--
--     Optional like the others: without it the screen keeps drawing the
--     gradient initial avatar and hides the upload tile, so an un-migrated
--     database still works, it just cannot store a photo.
-- -----------------------------------------------------------------------------
ALTER TABLE suppliers
    ADD COLUMN IF NOT EXISTS image_path VARCHAR(255) DEFAULT NULL AFTER address;


-- -----------------------------------------------------------------------------
--  4. suppliers — commercial detail
--     Fields a supplier record needs to be useful to accounting, rather than
--     just a contact list entry. All optional, all defaulted to NULL, so an
--     un-migrated database keeps its existing behaviour and the form simply
--     does not offer the fields.
--
--     tin            Tax Identification Number. A purchase is only claimable
--                    against a registered supplier, so this is what accounting
--                    asks for first.
--     payment_terms  'COD', '15 days', '30 days', ... When the payable falls
--                    due. Pairs with Accountant > Payables.
--     notes          Delivery quirks, minimum order, best time to call —
--                    whatever the fixed fields cannot hold.
-- -----------------------------------------------------------------------------
ALTER TABLE suppliers
    ADD COLUMN IF NOT EXISTS tin VARCHAR(30) DEFAULT NULL AFTER email;

ALTER TABLE suppliers
    ADD COLUMN IF NOT EXISTS payment_terms VARCHAR(40) DEFAULT NULL AFTER address;

ALTER TABLE suppliers
    ADD COLUMN IF NOT EXISTS notes VARCHAR(500) DEFAULT NULL AFTER payment_terms;


-- -----------------------------------------------------------------------------
--  Check the result — every column below should be listed.
-- -----------------------------------------------------------------------------
SHOW COLUMNS FROM products LIKE 'image_path';
SHOW COLUMNS FROM purchase_orders LIKE 'paid_at';
SHOW COLUMNS FROM suppliers LIKE 'image_path';
SHOW COLUMNS FROM suppliers LIKE 'tin';
SHOW COLUMNS FROM suppliers LIKE 'payment_terms';
SHOW COLUMNS FROM suppliers LIKE 'notes';
