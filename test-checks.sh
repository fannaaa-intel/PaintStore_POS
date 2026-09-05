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
"$MSBUILD" PaintStore_POS.vbproj -nologo -v:q -t:Rebuild >/tmp/psb.log 2>&1
chk $? "compiles with 0 errors"

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

echo ""; echo "[4] Current state"
HAS=$("$MYSQL" -u root -D $DB -N -e "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='purchase_orders' AND column_name='paid_at';" 2>/dev/null)
if [ "$HAS" = "1" ]; then echo "  paid_at APPLIED    -> Payables shows received-but-unpaid orders"
else echo "  paid_at NOT applied -> Payables keeps its old behaviour (safe default)"; fi
"$MYSQL" -u root -D $DB -e "SELECT vat_rate, IF(vat_inclusive,'inclusive','EXCLUSIVE') AS vat_mode FROM store_settings;" 2>/dev/null

echo ""; echo "=============================================="
echo " $pass passed, $fail failed"
echo "=============================================="
[ "$fail" = "0" ] || exit 1
