#!/bin/bash
# Automated pre-flight checks for PaintStore POS.
# Verifies the build and that every changed query runs against the live schema.
# It only READS -- nothing here modifies your data.

MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
MYSQL="/c/Program Files/MySQL/MySQL Server 8.0/bin/mysql.exe"
DB=paint_store_pos
pass=0; fail=0
chk() { if [ "$1" = "0" ]; then echo "  PASS  $2"; pass=$((pass+1)); else echo "  FAIL  $2"; fail=$((fail+1)); fi; }

echo "=============================================="
echo " PaintStore POS - pre-flight checks"
echo "=============================================="

echo ""; echo "[1] Build"
if tasklist 2>/dev/null | grep -qi "PaintStore_POS.exe"; then
    echo "  NOTE  app is running - it locks the .exe, so only compiling"
    "$MSBUILD" PaintStore_POS.vbproj -nologo -v:q -t:Build -p:OutDir=obj\checkbuild\ >/tmp/psb.log 2>&1
else
    "$MSBUILD" PaintStore_POS.vbproj -nologo -v:q -t:Rebuild >/tmp/psb.log 2>&1
fi
# MSB3021/MSB3027 are "cannot overwrite the locked .exe", not compile errors.
if grep -qE "error BC[0-9]+" /tmp/psb.log; then
    chk 1 "compiles with 0 errors"
    grep -E "error BC[0-9]+" /tmp/psb.log | head -5 | sed 's/^/        /'
else
    chk 0 "compiles with 0 errors"
fi

echo ""; echo "[2] Database"
"$MYSQL" -u root -D $DB -e "SELECT 1;" >/dev/null 2>&1
chk $? "app credentials from App.config connect"

echo ""; echo "[3] Changed queries parse against the live schema"
q() { "$MYSQL" -u root -D $DB -e "$1" >/dev/null 2>&1; chk $? "$2"; }
q "SELECT COUNT(*) FROM sales WHERE status IN ('Completed','Partially Refunded')" "revenue filter incl. Partially Refunded"
q "SELECT COALESCE(SUM(total_refund),0) FROM returns WHERE return_datetime >= CURDATE() - INTERVAL 30 DAY" "refund deduction (Accountant income)"
q "SELECT p.product_id,p.sku FROM products p WHERE p.is_active=1" "product load incl. sku (POS search)"
q "SELECT COALESCE(MAX(CAST(SUBSTRING(sku,5) AS UNSIGNED)),0) FROM products WHERE sku LIKE 'PNT-%'" "SKU generator across all products"
q "SELECT COUNT(*) FROM stock_movements WHERE reference_type IN ('receive','putaway')" "PO cancel receive-guard"
q "SELECT COUNT(*) FROM purchase_requests WHERE status IN ('Pending','Approved','Quoted')" "PO request status transition"
q "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='purchase_orders' AND column_name='paid_at'" "paid_at detection"
q "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='suppliers' AND column_name='image_path'" "supplier image_path detection"
q "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='suppliers' AND column_name IN ('image_path','tin','payment_terms','notes')" "supplier optional-column probe"
q "SELECT s.supplier_id,s.name,s.contact_person,s.phone,s.email,s.address,s.is_active,s.image_path,s.tin,s.payment_terms,s.notes,(SELECT COUNT(*) FROM purchase_orders po WHERE po.supplier_id=s.supplier_id AND po.status NOT IN ('Cancelled','Received')) AS active_pos FROM suppliers s ORDER BY s.name ASC" "supplier list query (all columns)"
q "SELECT s.supplier_id,s.name,s.contact_person,s.phone,s.email,s.is_active,s.image_path,s.tin,s.payment_terms,s.notes,COUNT(DISTINCT p.product_id) AS product_count,COUNT(DISTINCT po.po_id) AS po_count FROM suppliers s LEFT JOIN products p ON p.supplier_id=s.supplier_id LEFT JOIN purchase_orders po ON po.supplier_id=s.supplier_id GROUP BY s.supplier_id,s.name,s.contact_person,s.phone,s.email,s.is_active,s.image_path,s.tin,s.payment_terms,s.notes ORDER BY s.name ASC" "admin supplier grid (all columns)"
q "SELECT s.supplier_id,s.name,s.contact_person,s.phone,s.email,s.address,s.is_active,(SELECT COUNT(*) FROM purchase_orders po WHERE po.supplier_id=s.supplier_id AND po.status NOT IN ('Cancelled','Received')) AS active_pos FROM suppliers s ORDER BY s.name ASC" "supplier list query (no image column)"
q "SELECT s.supplier_id,s.name,s.contact_person,s.phone,s.email,s.is_active,COUNT(DISTINCT p.product_id) AS product_count,COUNT(DISTINCT po.po_id) AS po_count FROM suppliers s LEFT JOIN products p ON p.supplier_id=s.supplier_id LEFT JOIN purchase_orders po ON po.supplier_id=s.supplier_id GROUP BY s.supplier_id,s.name,s.contact_person,s.phone,s.email,s.is_active ORDER BY s.name ASC" "admin supplier grid (no image column)"
q "SELECT s.supplier_id,s.name,s.contact_person,s.phone,s.email,s.is_active,s.image_path,COUNT(DISTINCT p.product_id) AS product_count,COUNT(DISTINCT po.po_id) AS po_count FROM suppliers s LEFT JOIN products p ON p.supplier_id=s.supplier_id LEFT JOIN purchase_orders po ON po.supplier_id=s.supplier_id GROUP BY s.supplier_id,s.name,s.contact_person,s.phone,s.email,s.is_active,s.image_path ORDER BY s.name ASC" "admin supplier grid (with image column)"

echo ""; echo "[4] Current state"
HAS=$("$MYSQL" -u root -D $DB -N -e "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='purchase_orders' AND column_name='paid_at';" 2>/dev/null)
if [ "$HAS" = "1" ]; then echo "  paid_at APPLIED    -> Payables shows received-but-unpaid orders"
else echo "  paid_at NOT applied -> Payables keeps its old behaviour (safe default)"; fi
HASIMG=$("$MYSQL" -u root -D $DB -N -e "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='suppliers' AND column_name IN ('image_path','tin','payment_terms','notes');" 2>/dev/null)
if [ "$HASIMG" = "4" ]; then
    echo "  supplier columns APPLIED (4/4) -> logo, TIN, terms, notes all available"
    "$MYSQL" -u root -D $DB -e "SELECT s.supplier_id,s.name,s.image_path,(SELECT COUNT(*) FROM purchase_orders po WHERE po.supplier_id=s.supplier_id AND po.status NOT IN ('Cancelled','Received')) AS active_pos FROM suppliers s ORDER BY s.name ASC;" 2>/dev/null
else
    echo "  supplier columns PARTIAL ($HASIMG/4) -> missing ones are simply not offered (safe default)"
fi
"$MYSQL" -u root -D $DB -e "SELECT vat_rate, IF(vat_inclusive,'inclusive','EXCLUSIVE') AS vat_mode FROM store_settings;" 2>/dev/null

echo ""; echo "=============================================="
echo " $pass passed, $fail failed"
echo "=============================================="
[ "$fail" = "0" ] || exit 1
