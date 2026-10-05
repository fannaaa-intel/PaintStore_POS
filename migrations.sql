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

-- -----------------------------------------------------------------------------
--  13. supplier_payments - record what was actually paid, not just that it was
--
--     Settling a purchase order used to be a single stamp:
--
--         UPDATE purchase_orders SET paid_at = NOW()
--
--     That records THAT a PO was settled and nothing else. It cannot answer how
--     much was handed over, against which supplier document, by whom, on what
--     date, or by what method - so the only figure available to reporting was
--     purchase_orders.total_cost, the amount ORDERED. Ordered and paid are not
--     the same number.
--
--     Payment upon delivery is the store's normal arrangement: the goods arrive,
--     the supplier presents a receipt, and payment is made against THAT
--     document. Its amount is the authoritative one. It can legitimately differ
--     from the PO total - a short delivery, a price change since quoting, a
--     discount given at the door - and when it differs, the difference is the
--     single most useful number on the record. Overwriting it with the ordered
--     figure destroys exactly the evidence an audit looks for.
--
--     So the receipt amount is stored as given, the PO total is snapshotted
--     beside it, and the variance is derived from the two rather than being
--     silently reconciled away.
--
--     One row per payment, not one per PO: a partially settled order can take
--     a second payment later, and both belong on the record.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS supplier_payments (
    payment_id      INT(11)        NOT NULL AUTO_INCREMENT,
    po_id           INT(11)        NOT NULL,

    -- What the supplier's own document says. THIS is the amount paid, and it
    -- is never derived from the PO.
    amount_paid     DECIMAL(12,2)  NOT NULL,

    -- The PO total as it stood when payment was recorded. Snapshotted so the
    -- variance stays reconstructable even if the PO is edited afterwards.
    po_total        DECIMAL(12,2)  NOT NULL DEFAULT 0.00,

    -- The supplier's reference: their receipt, invoice or delivery-receipt
    -- number. This is what ties the row back to a piece of paper in a folder.
    reference_no    VARCHAR(60)    DEFAULT NULL,

    -- How it was paid. 'Payment on Delivery' is the store's normal case and the
    -- default; the rest are here so the column does not need widening the first
    -- time someone pays a deposit by transfer.
    method          ENUM('Payment on Delivery','Cash','Bank Transfer','Cheque','E-Wallet')
                    NOT NULL DEFAULT 'Payment on Delivery',

    -- When the money actually changed hands, which is not always when the row
    -- was typed in - a payment made at the door on Friday can be entered Monday.
    paid_on         DATE           NOT NULL,

    -- Free-text explanation, and the place a variance gets justified.
    remarks         VARCHAR(300)   DEFAULT NULL,

    recorded_by     INT(11)        DEFAULT NULL,
    recorded_at     DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP,

    PRIMARY KEY (payment_id),
    KEY idx_sp_po (po_id),
    KEY idx_sp_paid_on (paid_on),
    KEY idx_sp_user (recorded_by)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SHOW TABLES LIKE 'supplier_payments';

-- -----------------------------------------------------------------------------
--  14. purchase_request_items - a request is a LIST of products, not one
--
--     purchase_requests carries product_id and qty_requested directly on the
--     request row, so the table can only express "one product, one quantity".
--     A staff member needing five products had to raise five separate requests,
--     and the approver reviewed five unrelated rows with nothing tying them
--     together. Approving a basket was not possible because a basket could not
--     be represented.
--
--     Requesting is done by the INVENTORY role, from Low Stock Alerts, one
--     product at a time -- that screen is the only place a request has ever
--     been created.
--
--     This adds the line table a request needs, and backfills one line per
--     existing request so no history is lost.
--
--     WHY THE OLD COLUMNS STAY
--     purchase_requests.product_id and .qty_requested are NOT dropped. Six read
--     sites across Procurement, Admin and Inventory still join on product_id,
--     and dropping it would break all of them at once. Instead:
--
--       * every request keeps its first line mirrored into the old columns,
--         so existing queries keep returning exactly what they returned before
--       * new code reads purchase_request_items and sees the whole list
--
--     A single-line request therefore behaves identically under both readings.
--     A multi-line request shows its first line to old queries and all of its
--     lines to new ones, which is a narrowing, never a wrong number. The old
--     columns can be dropped once every read site has moved over.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS purchase_request_items (
    pr_item_id      INT(11)       NOT NULL AUTO_INCREMENT,
    request_id      INT(11)       NOT NULL,
    product_id      INT(11)       NOT NULL,
    qty_requested   INT(11)       NOT NULL DEFAULT 1,

    -- Why THIS line is on the request. The request-level note explains the
    -- basket; this explains one item in it.
    note            VARCHAR(200)  DEFAULT NULL,

    PRIMARY KEY (pr_item_id),
    KEY idx_pri_request (request_id),
    KEY idx_pri_product (product_id),
    CONSTRAINT fk_pri_request FOREIGN KEY (request_id)
        REFERENCES purchase_requests (request_id) ON DELETE CASCADE,
    CONSTRAINT fk_pri_product FOREIGN KEY (product_id)
        REFERENCES products (product_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Backfill: one line per existing request, mirroring what the request row
-- already said. Guarded so re-running the migration cannot duplicate lines.
INSERT INTO purchase_request_items (request_id, product_id, qty_requested, note)
SELECT pr.request_id, pr.product_id, pr.qty_requested, NULL
FROM purchase_requests pr
WHERE NOT EXISTS (
    SELECT 1 FROM purchase_request_items pri
    WHERE pri.request_id = pr.request_id
);

SHOW TABLES LIKE 'purchase_request_items';
SELECT COUNT(*) AS lines_backfilled FROM purchase_request_items;


-- -----------------------------------------------------------------------------
--  15. users.email - where account notices and verification codes are sent
--
--     Accounts are now created only by an admin (self sign-up was removed),
--     and the admin types the staff member's email on the New User form. It is
--     used for two things:
--       - the "your account was created" notice sent when the account is made
--       - the 6-digit code that must be entered before a password can change
--
--     UNIQUE so one mailbox can never receive another person's codes. NULL is
--     allowed (and several NULLs may coexist) so accounts that predate this
--     column keep working until an admin adds their email.
-- -----------------------------------------------------------------------------
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS email VARCHAR(120) DEFAULT NULL AFTER username;

ALTER TABLE users
    ADD UNIQUE INDEX IF NOT EXISTS uq_users_email (email);


-- -----------------------------------------------------------------------------
--  16. password_otp - one-time codes for the two-step password change
--
--     Changing a password now takes the current password AND a code sent to
--     the account's email. Only a hash of the code is stored, so reading this
--     table does not reveal a usable code. A code expires after 10 minutes,
--     allows 5 wrong guesses, and is marked used the moment it succeeds.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS password_otp (
    otp_id      INT(11)      NOT NULL AUTO_INCREMENT,
    user_id     INT(11)      NOT NULL,
    code_hash   VARCHAR(255) NOT NULL,
    expires_at  DATETIME     NOT NULL,
    attempts    INT(11)      NOT NULL DEFAULT 0,
    used_at     DATETIME     DEFAULT NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (otp_id),
    KEY idx_otp_user (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- -----------------------------------------------------------------------------
--  17. Cheque payments
--
--     A third payment type beside Cash and Reference. At checkout a Cheque
--     method asks for the ACCOUNT NAME on the cheque and the CHECK NUMBER.
--     The check number is stored in sales.reference_no - the column every
--     report already shows - and the account name in its own column.
--
--     A 'Cheque' method is added (active) so the till offers it straight away.
--     Admin can hide or rename it in Settings > Store & Payments.
-- -----------------------------------------------------------------------------
ALTER TABLE payment_methods
    MODIFY COLUMN type ENUM('Cash','Reference','Cheque') NOT NULL DEFAULT 'Reference';

ALTER TABLE sales
    ADD COLUMN IF NOT EXISTS cheque_account_name VARCHAR(120) DEFAULT NULL AFTER reference_no;

--  A method already called Cheque/Check becomes the cheque type instead of
--  a duplicate being added (payment_methods.name is UNIQUE).
UPDATE payment_methods SET type = 'Cheque'
    WHERE LOWER(name) IN ('cheque', 'check') AND type <> 'Cash';

INSERT INTO payment_methods (name, type, is_active)
SELECT 'Cheque', 'Cheque', 1 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM payment_methods WHERE type = 'Cheque')
  AND NOT EXISTS (SELECT 1 FROM payment_methods WHERE LOWER(name) = 'cheque');


-- -----------------------------------------------------------------------------
--  18. archive - the Recycle Bin
--
--     Nothing is deleted without a copy landing here first. Each row holds the
--     deleted record (and any child rows deleted with it, e.g. a colour
--     formula's components) as JSON, plus who deleted it and when.
--
--     Admin > Settings > Recycle Bin lists these rows and can Restore one
--     (puts the record back under its original id) or Delete Forever.
--
--     status  'Archived' - in the bin
--             'Restored' - put back; row kept as history
--             'Purged'   - admin emptied it; JSON cleared, row kept as history
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS archive (
    archive_id    INT(11)      NOT NULL AUTO_INCREMENT,
    source_table  VARCHAR(40)  NOT NULL,
    record_id     INT(11)      NOT NULL,
    label         VARCHAR(200) NOT NULL,
    data_json     LONGTEXT     DEFAULT NULL,
    deleted_by    INT(11)      DEFAULT NULL,
    deleted_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    status        ENUM('Archived','Restored','Purged') NOT NULL DEFAULT 'Archived',
    handled_by    INT(11)      DEFAULT NULL,
    handled_at    DATETIME     DEFAULT NULL,
    PRIMARY KEY (archive_id),
    KEY idx_archive_status (status),
    KEY idx_archive_source (source_table, record_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- -----------------------------------------------------------------------------
--  19. payment_method_requests - cashier asks, admin approves or rejects
--
--     Cashiers cannot change payment methods directly. From Settings >
--     Payment Methods they request a new method (Add) or ask for one to go
--     (Remove), saying why. The admin decides in Settings > Payment Requests:
--     Approve makes the change; Reject needs a reason, which the cashier sees.
--
--     outcome        what approving actually did (e.g. "hidden from the till -
--                    used by 12 sales" when a removal could not delete it)
--     reject_reason  required on Rejected
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS payment_method_requests (
    request_id     INT(11)      NOT NULL AUTO_INCREMENT,
    action         ENUM('Add','Remove') NOT NULL,
    method_id      INT(11)      DEFAULT NULL,
    name           VARCHAR(30)  NOT NULL,
    type           ENUM('Cash','Reference','Cheque') DEFAULT NULL,
    note           VARCHAR(255) DEFAULT NULL,
    reason         VARCHAR(255) NOT NULL,
    status         ENUM('Pending','Approved','Rejected') NOT NULL DEFAULT 'Pending',
    requested_by   INT(11)      DEFAULT NULL,
    requested_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    reviewed_by    INT(11)      DEFAULT NULL,
    reviewed_at    DATETIME     DEFAULT NULL,
    reject_reason  VARCHAR(255) DEFAULT NULL,
    outcome        VARCHAR(255) DEFAULT NULL,
    PRIMARY KEY (request_id),
    KEY idx_pmr_status (status),
    KEY idx_pmr_user (requested_by)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- -----------------------------------------------------------------------------
--  20. Report signatories
--
--     Every generated report (print, PDF, Excel) ends with a signature block:
--
--         Prepared by   the signed-in user and their role (automatic)
--         Checked by    set here
--         Approved by   set here
--
--     The accountant (or admin) fills in Checked / Approved in
--     Settings > Report Signatories. A blank name still prints a line to sign
--     on, so the block is usable before anyone has been named.
-- -----------------------------------------------------------------------------
ALTER TABLE store_settings
    ADD COLUMN IF NOT EXISTS sig_checked_name      VARCHAR(100) DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS sig_checked_position  VARCHAR(100) DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS sig_approved_name     VARCHAR(100) DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS sig_approved_position VARCHAR(100) DEFAULT NULL;

SHOW COLUMNS FROM store_settings LIKE 'sig_%';


-- -----------------------------------------------------------------------------
--  21. Opening cash check at the start of a shift
--
--     The cashier now COUNTS the drawer (bills and coins) when a shift starts,
--     and the count is compared with what the last closed shift on the same
--     register counted into it (shifts.counted_cash).
--
--       less than that  -> the shift cannot start. Cash may not leave the
--                          drawer between shifts; money taken out must go
--                          through Drop to Safe before the drawer is closed.
--       same or more    -> the shift starts; cash ADDED is allowed, stored
--                          here and noted in the Activity Log.
--
--     expected_opening  what the last shift left (NULL = nothing to compare)
--     opening_variance  counted - expected (0 or more; never negative)
-- -----------------------------------------------------------------------------
ALTER TABLE shifts
    ADD COLUMN IF NOT EXISTS expected_opening DECIMAL(10,2) DEFAULT NULL AFTER opening_float,
    ADD COLUMN IF NOT EXISTS opening_variance DECIMAL(10,2) DEFAULT NULL AFTER expected_opening;

SHOW COLUMNS FROM shifts LIKE '%opening%';

-- -----------------------------------------------------------------------------
--  22. Procurement revision - request approval and delivery problems
--
--     a) purchase_requests.review_note
--        Inventory sends a request; it stays 'Pending' until the ADMIN approves
--        or rejects it (Admin > Procurement Overview > Purchase Requests).
--        A rejection must say why - the reason is stored here and shown to the
--        requester in Low Stock Alerts > My Requests. While a request is still
--        Pending the requester may edit it or remove it; updated_at records
--        the last edit.
--
--        Requests that were already 'Pending' before this update were never
--        approved by anyone (there was no approval step), so they now wait for
--        the admin like any new request.
--
--     b) goods_receipt_issues
--        Receive Goods records, per product, units that arrived Defective or
--        were Missing (short delivery), what was wrong, and what happens next:
--          Return to Supplier  not stocked; the PO stays open for replacements
--          Accept as-is        stocked anyway, kept on record
--          Awaiting Supplier   missing units; the PO stays open for the rest
--        Shown in Delivery Tracking, the PO details window and Reports.
-- -----------------------------------------------------------------------------
ALTER TABLE purchase_requests
    ADD COLUMN IF NOT EXISTS review_note VARCHAR(255) DEFAULT NULL AFTER reviewed_at,
    ADD COLUMN IF NOT EXISTS updated_at  DATETIME     DEFAULT NULL AFTER review_note;

CREATE TABLE IF NOT EXISTS goods_receipt_issues (
    issue_id      INT(11)      NOT NULL AUTO_INCREMENT,
    receipt_id    INT(11)      DEFAULT NULL,
    po_id         INT(11)      NOT NULL,
    po_item_id    INT(11)      DEFAULT NULL,
    product_id    INT(11)      NOT NULL,
    issue_type    ENUM('Defective','Missing') NOT NULL,
    qty           INT(11)      NOT NULL,
    details       VARCHAR(300) DEFAULT NULL,
    action_taken  ENUM('Return to Supplier','Accept as-is','Awaiting Supplier') NOT NULL,
    recorded_by   INT(11)      DEFAULT NULL,
    recorded_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (issue_id),
    KEY idx_gri_po (po_id),
    KEY idx_gri_receipt (receipt_id),
    KEY idx_gri_product (product_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SHOW COLUMNS FROM purchase_requests LIKE 're%';
SHOW COLUMNS FROM goods_receipt_issues;

-- -----------------------------------------------------------------------------
--  23. Procurement - purchase requests and supplier payment records
--
--     a) purchase_requests.purpose
--        Why the request is being made (required on the form, up to 200
--        characters). Requests can now be sent by Procurement as well as
--        Inventory; both still wait for the admin's approval.
--
--     b) purchase_request_items.unit_price
--        The estimated unit cost entered on the request, so the request
--        shows an estimated total. A new PO made from the request starts
--        from this price.
--
--     c) supplier_payments.invoice_no / invoice_date
--        The supplier's invoice behind each payment. A PO can now be paid in
--        parts; its status (Unpaid / Partial / Paid) is worked out from the
--        payments recorded, so there is no status column to keep in step.
--        Older payments kept the invoice number in reference_no; they are
--        left as they are.
-- -----------------------------------------------------------------------------
ALTER TABLE purchase_requests
    ADD COLUMN IF NOT EXISTS purpose VARCHAR(200) DEFAULT NULL AFTER note;

ALTER TABLE purchase_request_items
    ADD COLUMN IF NOT EXISTS unit_price DECIMAL(10,2) DEFAULT NULL AFTER qty_requested;

ALTER TABLE supplier_payments
    ADD COLUMN IF NOT EXISTS invoice_no   VARCHAR(60) DEFAULT NULL AFTER reference_no,
    ADD COLUMN IF NOT EXISTS invoice_date DATE        DEFAULT NULL AFTER invoice_no;

SHOW COLUMNS FROM purchase_requests LIKE 'purpose';
SHOW COLUMNS FROM purchase_request_items LIKE 'unit_price';
SHOW COLUMNS FROM supplier_payments LIKE 'invoice%';

-- -----------------------------------------------------------------------------
--  24. Senior / PWD cardholder on the sale record
--
--     RA 9994 (Senior Citizens) and RA 10754 (PWD) want the cardholder's name
--     and ID number on the sale, not only on the printed receipt. Until now
--     they were typed at the till, printed once, and never stored - a reprint
--     or an audit had nothing to show.
--
--     Filled only on VAT-exempt (Senior / PWD) sales. Also kept on a HELD
--     sale, so retrieving it brings the cardholder back with the cart.
--
--     Optional like the rest: without these columns the till saves the sale
--     as before and the details still print on the receipt.
-- -----------------------------------------------------------------------------
ALTER TABLE sales
    ADD COLUMN IF NOT EXISTS exempt_holder_name VARCHAR(120) DEFAULT NULL AFTER cheque_account_name,
    ADD COLUMN IF NOT EXISTS exempt_id_no       VARCHAR(60)  DEFAULT NULL AFTER exempt_holder_name;

SHOW COLUMNS FROM sales LIKE 'exempt%';
