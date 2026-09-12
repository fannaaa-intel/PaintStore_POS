-- =============================================================================
--  PaintStore POS — optional schema additions
--
--  HOW TO RUN
--    phpMyAdmin : select the paint_store_pos database on the left FIRST,
--                 then open the SQL tab, paste this file, press Go.
--    Command line:
--                 C:\xampp\mysql\bin\mysql.exe -u root paint_store_pos < migrations.sql
--
--  !! USE THE XAMPP CLIENT ONLY !!
--
--  This database runs on XAMPP MariaDB 10.4 (port 3306). If MySQL Server 8.0 is
--  also installed, its client sits at
--      C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe
--  and it must NOT be used against this database. MySQL 8.0 ALTER TABLE
--  statements make MariaDB write an "instant ALTER" metadata header it cannot
--  read back, and the table then fails with
--      #1932 - Table '...' doesn't exist in engine
--  even though the .ibd data file is perfectly intact. Recovering from that
--  needs a DISCARD / IMPORT TABLESPACE rebuild.
--
--  Always use:  C:\xampp\mysql\bin\mysql.exe
--  Check with:  SELECT @@version;   -- must say MariaDB
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
--  9. suppliers - business registration
--
--     A supplier is a REGISTERED BUSINESS, not just a contact. Accounting and
--     BIR audit both work from the registration papers, and until now the form
--     only held a trading name and a TIN, so none of the rest could be
--     recorded against the row it belongs to.
--
--     company_name  The registered business name on the DTI/SEC papers, which
--                   is often NOT the name the store calls them by. "name"
--                   stays the everyday trading name so nothing that already
--                   displays a supplier changes.
--     head_name     Proprietor / President / authorised signatory - the person
--                   who actually signs a contract. Distinct from
--                   contact_person, who is only the day-to-day contact
--                   (a sales rep cannot bind the company).
--     dti_no        DTI Certificate of Business Name Registration number
--                   (SEC registration for a corporation).
--     bir_no        BIR Certificate of Registration (Form 2303) number. Kept
--                   apart from tin: the TIN identifies the taxpayer, the COR
--                   number identifies the registration document, and an audit
--                   asks for both.
--
--     All optional and NULL by default, like every other section here: the
--     app probes for them and simply does not offer the fields when they are
--     absent, so an un-migrated database behaves exactly as before.
-- -----------------------------------------------------------------------------
ALTER TABLE suppliers
    ADD COLUMN IF NOT EXISTS company_name VARCHAR(150) DEFAULT NULL AFTER name;

ALTER TABLE suppliers
    ADD COLUMN IF NOT EXISTS head_name VARCHAR(120) DEFAULT NULL AFTER contact_person;

ALTER TABLE suppliers
    ADD COLUMN IF NOT EXISTS dti_no VARCHAR(60) DEFAULT NULL AFTER tin;

ALTER TABLE suppliers
    ADD COLUMN IF NOT EXISTS bir_no VARCHAR(60) DEFAULT NULL AFTER dti_no;


-- -----------------------------------------------------------------------------
--  10. goods_receipts - who accepted a delivery, and their attestation
--
--     Receiving goods is the moment the store takes ownership and the payable
--     becomes real. Before this table the only trace was stock_movements.user_id
--     - whoever happened to be signed in. That is not an attestation: it does
--     not say a person looked at the delivery, nor that the goods were sound,
--     and a till left signed in could accept a delivery on someone else's name.
--
--     One row per RECEIPT EVENT (a PO received in two deliveries gets two
--     rows), so a partial-delivery history stays intact.
--
--     received_by     The signed-in user who confirmed. They re-enter their own
--                     password in the confirm dialog, so this is an
--                     authenticated identity rather than an ambient session.
--     receiver_name   The name typed on the dialog - the person who physically
--                     checked the goods at the dock, who may be a warehouse
--                     hand rather than the account holder.
--     condition_note  'Good'       - complete and undamaged.
--                     'Discrepancy' - short, damaged or wrong; remarks say what.
--     remarks         Free text detailing any discrepancy.
--     attested        Always 1 for a saved receipt: the dialog will not commit
--                     without the certification ticked. Stored so the record
--                     carries the attestation itself and not just its effect.
--     total_units     Units accepted in THIS event, so the row is meaningful
--                     without re-deriving it from stock_movements.
--
--     Optional like the rest - the app checks the table exists and falls back
--     to the previous no-attestation behaviour when it does not.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS goods_receipts (
    receipt_id     INT(11)      NOT NULL AUTO_INCREMENT,
    po_id          INT(11)      NOT NULL,
    received_by    INT(11)      DEFAULT NULL,
    receiver_name  VARCHAR(120) NOT NULL,
    condition_note ENUM('Good','Discrepancy') NOT NULL DEFAULT 'Good',
    remarks        VARCHAR(500) DEFAULT NULL,
    attested       TINYINT(1)   NOT NULL DEFAULT 1,
    total_units    INT(11)      NOT NULL DEFAULT 0,
    received_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (receipt_id),
    KEY idx_gr_po (po_id),
    KEY idx_gr_user (received_by)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;


-- -----------------------------------------------------------------------------
--  11. Cost tracking — real gross margin
--
--  Before this, products carried only unit_price (what the customer pays). Cost
--  existed solely on purchase orders, so the Accountant dashboard reported
--  "Net Profit" as revenue minus every PO raised in the period. Buying stock
--  therefore looked like a loss even when nothing had been sold at a loss, and
--  a month with no restocking looked unusually profitable.
--
--  Two columns fix it:
--    products.cost_price   - current replacement cost, kept up to date by
--                            receiving (see section 12 below)
--    sale_items.unit_cost  - the cost AT THE MOMENT OF SALE. Margin has to be
--                            computed against what that unit actually cost, not
--                            against today's cost_price: re-pricing stock would
--                            otherwise silently rewrite last month's reported
--                            profit.
-- -----------------------------------------------------------------------------
SET @c := (SELECT COUNT(*) FROM information_schema.columns
           WHERE table_schema = DATABASE() AND table_name = 'products'
             AND column_name = 'cost_price');
SET @s := IF(@c = 0,
    'ALTER TABLE products ADD COLUMN cost_price DECIMAL(10,2) NOT NULL DEFAULT 0.00 AFTER unit_price',
    'SELECT "products.cost_price already present"');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

SET @c := (SELECT COUNT(*) FROM information_schema.columns
           WHERE table_schema = DATABASE() AND table_name = 'sale_items'
             AND column_name = 'unit_cost');
SET @s := IF(@c = 0,
    'ALTER TABLE sale_items ADD COLUMN unit_cost DECIMAL(10,2) NOT NULL DEFAULT 0.00 AFTER unit_price',
    'SELECT "sale_items.unit_cost already present"');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

-- Seed cost_price from the most recent purchase of each product, so margin is
-- meaningful straight away instead of reading as 100% until the next delivery.
UPDATE products p
JOIN (
    SELECT poi.product_id, poi.unit_cost
    FROM po_items poi
    JOIN purchase_orders po ON po.po_id = poi.po_id
    JOIN (
        SELECT poi2.product_id, MAX(po2.po_id) AS latest_po
        FROM po_items poi2
        JOIN purchase_orders po2 ON po2.po_id = poi2.po_id
        WHERE po2.status <> 'Cancelled'
        GROUP BY poi2.product_id
    ) newest ON newest.product_id = poi.product_id AND newest.latest_po = po.po_id
) lastcost ON lastcost.product_id = p.product_id
SET p.cost_price = lastcost.unit_cost
WHERE p.cost_price = 0;

-- Backfill historical sales with the product's cost so past periods report a
-- margin rather than a 100%-profit distortion. Only rows never set are touched.
UPDATE sale_items si
JOIN products p ON p.product_id = si.product_id
SET si.unit_cost = p.cost_price
WHERE si.unit_cost = 0;


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
SHOW COLUMNS FROM suppliers LIKE 'company_name';
SHOW COLUMNS FROM suppliers LIKE 'head_name';
SHOW COLUMNS FROM suppliers LIKE 'dti_no';
SHOW COLUMNS FROM suppliers LIKE 'bir_no';
SHOW TABLES LIKE 'goods_receipts';
SHOW COLUMNS FROM products LIKE 'cost_price';
SHOW COLUMNS FROM sale_items LIKE 'unit_cost';


-- -----------------------------------------------------------------------------
--  12. goods_receipts.phase - separate the dock receipt from the shelf put-away
--
--     Receiving is two phases in this system, and each is a distinct moment of
--     accountability:
--
--       'receive'  Procurement confirms goods arrived from the supplier. Writes
--                  stock_movements (reference_type 'receive'). Ownership and
--                  the payable become real here.
--       'putaway'  Inventory physically moves the goods dock -> shelf. Writes
--                  stock_movements (reference_type 'putaway') and is the ONLY
--                  place products.stock_qty increases.
--
--     Section 10 recorded an attested receiver for the first phase only. The
--     second had no attestation at all: it stamped stock_movements.user_id,
--     whoever happened to be signed in - exactly the weakness section 10 was
--     written to close, still open on the phase that moves live stock.
--
--     One row per receipt EVENT already, so a PO that is received once and
--     shelved in two trips gets three rows. phase says which kind each is.
--
--     DEFAULT 'receive' backfills every existing row correctly: before this
--     column only dock receipts were ever written.
-- -----------------------------------------------------------------------------
ALTER TABLE goods_receipts
    ADD COLUMN IF NOT EXISTS phase ENUM('receive','putaway')
        NOT NULL DEFAULT 'receive' AFTER po_id;

-- Reading a PO's receipt history is always "this PO, this phase, in order",
-- so the two columns are indexed together rather than separately.
ALTER TABLE goods_receipts
    ADD INDEX IF NOT EXISTS idx_gr_po_phase (po_id, phase);

SHOW COLUMNS FROM goods_receipts LIKE 'phase';
