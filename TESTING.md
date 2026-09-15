# Test Plan — Procurement Revisions

Covers the three procurement changes plus the Purchase Order screen fixes.

---

## Already verified against the database (2026-09-16)

The data layer was tested directly — real rows written, read back, then removed.
These need **no** manual repeat; they are recorded here so you know what is
already proven and can spend your time on the screens instead.

| Test | Result |
|---|---|
| 1.2 Search by name / SKU / no-match / inactive excluded | ✅ pass |
| 1.6 Three-product request saves as ONE request with 3 lines | ✅ pass |
| 1.6 Admin view reads "3 products", 12 total units, all 3 listed | ✅ pass |
| 1.8 Duplicate guard finds a product sitting 2nd on a basket | ✅ pass |
| 2.2 "2 suppliers bidding" count | ✅ pass |
| 2.3 Grid values and cheapest-per-item | ✅ pass |
| 2.4 Recommendation card, including the ₱250 trade-off line | ✅ pass |
| 2.5 **Coverage rule** — rejected the cheaper ₱2,200 supplier covering 2 of 3 | ✅ pass |
| 2.6 Un-quoted product shows a dash, never ₱0.00 | ✅ pass |
| 2.7 Rejected bid stays visible but cannot win | ✅ pass |
| 3.2 Variance readout — match / less / more / letters / negative | ✅ pass |
| 3.3 Variance with empty remarks is blocked; with a reason it saves | ✅ pass |
| 3.5 PO clears from payables; double-settle blocked | ✅ pass |
| 3.6 Receipt amount, PO total and reason all stored | ✅ pass |
| 5.10 Legacy single-product request still reads correctly | ✅ pass |
| Regression suite (`test-checks.sh`) | ✅ 35/35 |

**What still needs a human:** everything visual and interactive — the forms,
buttons, placeholder, layout, and the scrollbar. Those are Parts 1.1–1.5, 1.7,
2.1, 3.1, 3.4, Part 4 and Part 5. The database work behind them is done.

**Before you start**

- XAMPP MySQL must be running (XAMPP Control Panel → MySQL → Start).
- Run `PaintStore_POS.exe`, or open the solution in Visual Studio and press F5.
- Logins are `procurement`, `inventory`, `accountant` — ask the admin for passwords.

Tick each ✅ as you go. If something fails, note **what you did**, **what you
expected**, and **what happened** — that is what makes a report fixable.

---

## Part 1 — Purchase Request with multiple products

**Sign in as `inventory`.**

### 1.1 Open the form

1. Click **Low Stock Alerts** in the left menu.
2. Look at the top right.

✅ A green **+ New Request** button sits to the right of *Copy order list*.

> **Note:** every product is currently above its reorder level, so the alerts
> list will be empty and no per-row **Request** pill appears. That is correct
> behaviour, not a bug. Test 1.7 covers the pill separately.

3. Click **+ New Request**.

✅ A window opens titled **New Purchase Request**, with a search box, an empty
items area, and a **Send for Approval** button.

### 1.2 Product search

1. Click the search box and type `oc`.

✅ Results appear showing **Ocean Breeze**, with its SKU and current stock.

2. Clear it and type `PNT-0002`.

✅ **Soft Sage** appears — searching by SKU works, not just by name.

3. Clear it and type `zzzz`.

✅ Reads *"No products match that search."* — no crash, no empty box.

4. Type a single letter, e.g. `o`.

✅ Nothing appears. Search needs 2+ characters, by design.

### 1.3 Add several products

1. Search `ocean` → click **Ocean Breeze**.

✅ It moves into the list below. Search box clears. Quantity shows **1**.

2. Add **Soft Sage** and **Warm Ivory** the same way.

✅ Three products listed, each with its own quantity box and an ✕ to remove.

✅ Under the list: *"3 products • 3 units • about ₱1,875.00 at current prices"*.

### 1.4 Quantities

1. Change Ocean Breeze's quantity to **10**.

✅ Its line value updates, and the summary line recalculates immediately.

2. Search `ocean` again and click **Ocean Breeze** a second time.

✅ **No duplicate row.** The existing line's quantity increases instead.
   *(Two rows for one product would be ambiguous to whoever fills the order.)*

3. Click ✕ on Warm Ivory.

✅ Row disappears; totals drop to 2 products.

### 1.5 Guard rails

1. Remove all products, then click **Send for Approval**.

✅ Warns *"Add at least one product before sending the request."* Nothing saves.

2. Add a product, then click **Cancel**.

✅ Asks to confirm before discarding. Click **No** — your list survives.

### 1.6 Send it

1. With 3 products on the list, click **Send for Approval**.
2. Confirm.

✅ Success message naming the product and unit counts. Window closes.

**Now check the approver's side. Sign out, sign in as `admin`.**

3. Go to **Procurement Overview** → **Purchase Requests** tab.

✅ **One** row, not three.

✅ It reads **"3 products"** — not just the first product's name.

✅ Underneath, all three are listed: *Ocean Breeze × 10 • Soft Sage × 1 • …*

✅ The quantity column shows the **total units across all three**.

> This is the heart of the change: one basket, one row, one approval.

### 1.7 The per-row Request pill

This only appears when something is genuinely low. To test it, ask an admin to
lower a product's stock below its reorder level, or raise its reorder level
above current stock. Then, as `inventory`:

1. Open **Low Stock Alerts** — the item now appears.
2. Click its **Request** pill.

✅ The same request form opens **with that product already on the list** and a
   suggested quantity filled in.

✅ You can still search and add more products before sending.

### 1.8 Duplicate protection

1. Send a request containing a product that is **not** first on the list
   (e.g. add Ocean Breeze, then Soft Sage — Soft Sage is second).
2. Go back to Low Stock Alerts and find **Soft Sage**.

✅ Its pill reads **"Requested ✓"**, not **"Request"**.

> This is the bug that was fixed: previously only the *first* product on a
> request was recognised, so a second-position item could be requested twice.

---

## Part 2 — Compare supplier prices

**Setup required.** The comparison needs **two or more quotations on the same
request**, and there are currently none. Create them first.

### 2.1 Create the test data

**Sign in as `procurement`.**

1. First, make sure an **Approved** request exists. As `admin`, approve one of
   the requests you sent in Part 1.
2. Go to **Purchase Orders** → **New Purchase Order**.
3. Pick supplier **Test Name Name name**.
4. Click **Add Quote**.
5. Choose the approved request, enter a unit price (e.g. `500`), **Save Quote**.
6. Repeat from step 2 with the **other** supplier (**Valorant**) on the **same
   request**, with a **different** price (e.g. `450`).

✅ Two quotations now exist on one request.

### 2.2 Spot a contested request

1. Go to **Quotations** in the left menu.

✅ Rows on that request show **"2 suppliers bidding"** in green.

### 2.3 Open the comparison

1. Click either quotation to open it.
2. Find the **Compare Suppliers** button.

✅ The button is there.

> If only one quotation exists on a request, the button is hidden — there is
> nothing to compare against. That is intended.

3. Click it.

✅ A grid appears: **products down the left, suppliers across the top**.

✅ The **cheapest price on each row is highlighted green** and slightly larger.

✅ At the bottom, each supplier's **total** with a **✓ cheapest** marker.

### 2.4 The recommendation

✅ A **Recommendation** card at the top names one supplier and their total.

✅ If a rival beat them on any item, an amber line says so, e.g.
   *"Another supplier is cheaper on 1 item, worth ₱250.00 — a purchase order
   goes to one supplier, so this is the cost of consolidating."*

### 2.5 The coverage rule — the important one

Make one supplier quote **fewer products** than the other, but at a **lower
total**. For example: Supplier A quotes 3 products for ₱3,250; Supplier B
quotes only 2 for ₱2,200.

✅ The recommendation picks **Supplier A** — the one covering more products —
   **not** the smaller number.

✅ Supplier B's column shows **"2 of 3 items"** under its total.

> A supplier who bid on half the request is cheaper for the wrong reason. If
> the screen recommends the ₱2,200 supplier here, that is a **bug** — report it.

### 2.6 Products a supplier didn't quote

✅ Any product a supplier did not bid on shows **—** and *"not quoted"*.

✅ It never shows **₱0.00**. *(No bid and free are not the same thing.)*

### 2.7 Rejected bids stay visible

1. Reject one quotation, then reopen the comparison.

✅ The rejected supplier's column is still there, greyed, labelled *rejected*.

✅ It **cannot** win the highlight or the recommendation.

---

## Part 3 — Payment on delivery

**Sign in as `accountant`.**

### 3.1 Open the payment form

1. Go to **Payables**.

✅ **PO-260624221846** is listed — supplier *Test Name Name name*, ₱9,990.00,
   status **Received**.

2. Click the row, then confirm when asked to record the payment.

✅ A **Record Payment** window opens, pre-filled with **9990.00**.

### 3.2 The variance readout — the point of this screen

1. Leave the amount at `9990.00`.

✅ Green: *"✓ Matches the purchase order exactly."*

2. Change it to `9500`.

✅ Amber: *"₱490.00 LESS than ordered. Give the reason in remarks before saving."*

3. Change it to `10500`.

✅ Amber: *"₱510.00 MORE than ordered."*

4. Type letters instead of a number.

✅ Red: *"Enter the amount printed on the receipt."*

### 3.3 Variance needs a reason

1. Set the amount to `9500`, leave **Remarks empty**, click **Record Payment**.

✅ **Blocked.** A message asks for a reason — short delivery, price change,
   discount — and focus moves to Remarks.

> The dialog does not block the payment. It blocks the *silence* around it:
> an unexplained gap between ordered and paid is what an audit asks about.

2. Type a reason, e.g. `Short delivery — 2 tins damaged, returned at the door`.
3. Click **Record Payment** again.

✅ Saves. Confirmation mentions the **₱490.00 shortfall on record**.

### 3.4 Other fields

✅ **Method** defaults to **Payment on Delivery**, with Cash / Bank Transfer /
   Cheque / E-Wallet also available.

✅ **Date paid** defaults to today and **cannot be set to a future date**.
   *(A payment that has not happened yet is not a payment.)*

✅ **Receipt / invoice number** accepts the supplier's reference.

### 3.5 It cleared

1. Return to **Payables**.

✅ The PO is **gone** from the payable list — it is settled.

### 3.6 Verify what was stored (optional, needs DB access)

```sql
SELECT po_id, amount_paid, po_total,
       ROUND(amount_paid - po_total, 2) AS variance,
       reference_no, method, paid_on, remarks
FROM supplier_payments;
```

✅ Stores the **receipt's** amount, the **PO total** beside it, and your reason.

> The old behaviour saved only a timestamp — no amount, no method, no
> reference. Reporting could only ever reach the amount *ordered*.

---

## Part 4 — Purchase Order screen fixes

**Sign in as `procurement`** → **Purchase Orders** → **New Purchase Order**.

### 4.1 The search is findable

✅ Under **Line Items** there is a caption reading **SEARCH PRODUCT**.

✅ The box shows grey placeholder text: *"Type a product name or SKU"*.

✅ The placeholder is **fully visible** — not clipped at the bottom.

> Previously this was an unlabelled empty dropdown, while the empty-state
> message told you to "use the product search" — a control by that name
> appeared nowhere on screen.

1. Click the box.

✅ It focuses, the list drops open, and the placeholder disappears.

2. Click away, leaving it empty.

✅ Placeholder returns.

3. Type `war`.

✅ Filters to **Warm Ivory**.

### 4.2 Empty-state wording

✅ With no pending requests, the left panel names a control you can see:
   *"Use Search Product, under Line Items, to add items manually."*

✅ With no line items: *"Add from a pending request, or type in Search Product above."*

### 4.3 No scrollbar on the line list

1. Add **4 or more** products to the order.

✅ **No scrollbar** appears at the right of the line list.

✅ The **TOTAL** bar stays pinned at the bottom.

2. Hover the list and scroll the mouse wheel.

✅ The rows still scroll — the bar is hidden, not the scrolling.

3. Resize the window narrower and wider.

✅ Still no scrollbar at any size.

> Three approaches were tried here; the first two let the bar reappear on
> resize or rebuild. If you ever see it come back, that is worth reporting.

---

## Part 5 — Nothing else broke

These worked before the changes and must still work.

| # | Check | ✅ |
|---|---|---|
| 5.1 | **POS** — ring up a sale, take payment, print receipt | |
| 5.2 | **Inventory** — item list, stock levels, adjustments | |
| 5.3 | **Receive Goods** — receive against a PO | |
| 5.4 | **Incoming Stock** — put away received goods | |
| 5.5 | **Suppliers** — add, edit, view | |
| 5.6 | **Admin → Procurement Overview** — all four tabs load | |
| 5.7 | **Procurement Dashboard** — pending-request count is right | |
| 5.8 | **Accountant** — reports and dashboard figures | |
| 5.9 | **Login** — all roles, plus forgot-password | |

### 5.10 Existing purchase requests

✅ The original request (product **TEST**, 5 units) still displays correctly
   everywhere — Admin overview, Procurement dashboard, PO builder.

> Old single-product requests were backfilled into the new structure. They must
> read exactly as they did before.

---

## Reporting a problem

Include:

1. **Where** — screen and role, e.g. *"Accountant → Payables → payment form"*
2. **What you did** — the steps
3. **What you expected**
4. **What happened** — exact message text, or a screenshot
5. Whether it happens **every time** or just once

---

## Known state of the test database

At the time of writing:

- **7 active products**, all currently **above** their reorder level
  → Low Stock Alerts is empty until stock is lowered (see 1.7)
- **2 suppliers** — *Test Name Name name*, *Valorant*
- **1 purchase request** (product TEST, Converted)
- **0 quotations** → Part 2 needs the setup in 2.1 first
- **1 payable PO** — PO-260624221846, ₱9,990.00
- **0 supplier payments** recorded yet
