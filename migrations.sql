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
--  6. users — self-service sign-up and admin approval
--
--     Sign-up writes a row with status='Pending'. The user cannot log in until
--     an admin approves it in Admin > User Management > Pending Approval.
--
--     DEFAULT 'Approved' matters: every user that already exists keeps working
--     exactly as before after this migration runs. Only new sign-ups are
--     pending. The backfill below covers rows created before the default
--     applied.
--
--     requested_role_id is what the applicant ASKED for; role_id is what the
--     admin actually granted. Keeping both means the pending list can show
--     "requested Cashier" while the admin stays free to approve a
--     different role.
-- -----------------------------------------------------------------------------
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS status ENUM('Pending','Approved','Rejected')
        NOT NULL DEFAULT 'Approved' AFTER is_active;

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS requested_role_id INT(11) DEFAULT NULL AFTER status;

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS reviewed_by INT(11) DEFAULT NULL AFTER requested_role_id;

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS reviewed_at DATETIME DEFAULT NULL AFTER reviewed_by;

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS reject_reason VARCHAR(255) DEFAULT NULL AFTER reviewed_at;

--  Force a password change after an admin-issued temporary password.
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS must_change_password TINYINT(1) NOT NULL DEFAULT 0
        AFTER reject_reason;

--  Any row that predates the status column is an existing, working account.
UPDATE users SET status = 'Approved' WHERE status IS NULL OR status = '';


-- -----------------------------------------------------------------------------
--  7. password_reset_requests
--
--     "Forgot password?" on the login screen files a row here instead of
--     telling the user to phone someone. The admin sees it in
--     Admin > User Management > Password Requests.
--
--     username_typed is stored as-is even when it matches no account, so the
--     login screen can show the same neutral confirmation either way and never
--     reveal which usernames exist. user_id stays NULL in that case.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS password_reset_requests (
    request_id    INT(11)      NOT NULL AUTO_INCREMENT,
    user_id       INT(11)      DEFAULT NULL,
    username_typed VARCHAR(50) NOT NULL,
    status        ENUM('Pending','Handled','Dismissed') NOT NULL DEFAULT 'Pending',
    requested_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    handled_by    INT(11)      DEFAULT NULL,
    handled_at    DATETIME     DEFAULT NULL,
    PRIMARY KEY (request_id),
    KEY idx_prr_status (status),
    KEY idx_prr_user (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;



-- -----------------------------------------------------------------------------
--  8. payment_methods — behaviour, availability and a note
--
--     A payment method used to be just a name, and the rest of the app GUESSED
--     how it behaved from that name: anything containing "cash" was treated as
--     cash (asks amount tendered, gives change, counts in the cash drawer),
--     everything else asked for a reference number. That mis-handles real
--     names — "GCash" contains "cash" but is an e-wallet that needs a
--     reference, not change.
--
--     type       'Cash'      -> checkout asks amount tendered and gives change,
--                              and the method counts in the cash drawer.
--                'Reference' -> checkout asks for a reference number, no change.
--                The DEFAULT is 'Reference': a brand-new method is the safe,
--                non-cash kind until an admin says otherwise.
--     is_active  0 hides the method from the till without deleting it, so old
--                sales that reference it stay intact.
--     note       Free text shown to the cashier at payment — e.g. the number
--                to send an e-wallet transfer to.
--
--     Optional like the rest: the app checks whether these columns exist and
--     falls back to the old name-guessing when they are absent, so an
--     un-migrated database keeps working exactly as before.
-- -----------------------------------------------------------------------------
ALTER TABLE payment_methods
    ADD COLUMN IF NOT EXISTS type ENUM('Cash','Reference') NOT NULL DEFAULT 'Reference' AFTER name;

ALTER TABLE payment_methods
    ADD COLUMN IF NOT EXISTS is_active TINYINT(1) NOT NULL DEFAULT 1 AFTER type;

ALTER TABLE payment_methods
    ADD COLUMN IF NOT EXISTS note VARCHAR(255) DEFAULT NULL AFTER is_active;

--  Backfill: the row whose name reads as cash becomes type='Cash'; every other
--  existing method keeps the safe 'Reference' default. Runs harmlessly again.
UPDATE payment_methods SET type = 'Cash'
    WHERE LOWER(name) LIKE '%cash%' AND LOWER(name) NOT LIKE '%gcash%';


-- -----------------------------------------------------------------------------
--  Check the result — every column below should be listed.
-- -----------------------------------------------------------------------------
SHOW COLUMNS FROM products LIKE 'image_path';
SHOW COLUMNS FROM purchase_orders LIKE 'paid_at';
SHOW COLUMNS FROM suppliers LIKE 'image_path';
SHOW COLUMNS FROM suppliers LIKE 'tin';
SHOW COLUMNS FROM suppliers LIKE 'payment_terms';
SHOW COLUMNS FROM suppliers LIKE 'notes';
SHOW COLUMNS FROM users LIKE 'status';
SHOW COLUMNS FROM users LIKE 'requested_role_id';
SHOW COLUMNS FROM users LIKE 'must_change_password';
SHOW TABLES LIKE 'password_reset_requests';
SHOW COLUMNS FROM payment_methods LIKE 'type';
SHOW COLUMNS FROM payment_methods LIKE 'is_active';
SHOW COLUMNS FROM payment_methods LIKE 'note';
