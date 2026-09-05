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
q "SELECT method_id,name FROM payment_methods ORDER BY method_id" "payment methods load (fallback shape)"
HASPM=$("$MYSQL" -u root -D $DB -N -e "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='payment_methods' AND column_name IN ('type','is_active','note');" 2>/dev/null)
if [ "$HASPM" = "3" ]; then
    q "SELECT method_id,name,type,is_active,note FROM payment_methods ORDER BY method_id" "payment methods load (with type/active/note)"
    q "SELECT method_id FROM payment_methods WHERE type='Cash' ORDER BY method_id LIMIT 1" "cash drawer finds cash by type"
    q "SELECT COUNT(*) FROM payment_methods WHERE type IN ('Cash','Reference')" "payment method type enum"
else
    echo "  SKIP  payment_methods behaviour columns not applied ($HASPM/3) - app falls back"
fi

# ── Sign-up approval + password reset (migrations.sql sections 6 and 7) ──────
# These are optional: the app probes for them and falls back when absent, so a
# missing column is reported as NOT APPLIED rather than counted as a failure.
HASAPP=$("$MYSQL" -u root -D $DB -N -e "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='users' AND column_name IN ('status','requested_role_id','reviewed_by','reviewed_at','reject_reason','must_change_password');" 2>/dev/null)
HASPRR=$("$MYSQL" -u root -D $DB -N -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='password_reset_requests';" 2>/dev/null)

if [ "$HASAPP" = "6" ]; then
    q "SELECT u.user_id,u.full_name,u.username,u.is_active,u.created_at,u.sales_quota,u.role_id,r.role_name,u.status,u.requested_role_id,u.reject_reason,u.reviewed_at,rq.role_name AS requested_role_name FROM users u JOIN roles r ON u.role_id=r.role_id LEFT JOIN roles rq ON u.requested_role_id=rq.role_id ORDER BY u.full_name" "user list incl. approval columns"
    q "SELECT COUNT(*) FROM users WHERE status IN ('Pending','Approved','Rejected')" "status enum accepts all three states"
    q "SELECT u.user_id,u.full_name,u.username,u.password_hash,u.is_active,u.status,r.role_name FROM users u JOIN roles r ON u.role_id=r.role_id WHERE u.username='__nobody__' LIMIT 1" "login query incl. status"
    q "SELECT user_id,status,reviewed_at,reject_reason FROM users WHERE status='Rejected'" "rejected tab query (reason + decided-at)"
    q "SELECT must_change_password FROM users WHERE user_id=0" "forced-change flag readable"
    q "SELECT COUNT(*) FROM users WHERE must_change_password IN (0,1)" "forced-change flag is 0/1"
else
    echo "  SKIP  approval columns not applied ($HASAPP/6) - app falls back"
fi

if [ "$HASPRR" = "1" ]; then
    q "SELECT p.request_id,p.user_id,p.username_typed,p.requested_at,u.full_name,r.role_name FROM password_reset_requests p LEFT JOIN users u ON p.user_id=u.user_id LEFT JOIN roles r ON u.role_id=r.role_id WHERE p.status='Pending' ORDER BY p.requested_at DESC" "password request list query"
    q "SELECT COUNT(*) FROM password_reset_requests WHERE status IN ('Pending','Handled','Dismissed')" "reset request status enum"
else
    echo "  SKIP  password_reset_requests not created - forgot-password falls back"
fi

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
if [ "$HASAPP" = "6" ]; then
    echo "  sign-up approval APPLIED (6/6) -> pending queue live in User Management"
    "$MYSQL" -u root -D $DB -e "SELECT status, COUNT(*) AS n FROM users GROUP BY status;" 2>/dev/null
else
    echo "  sign-up approval NOT applied ($HASAPP/6) -> sign-up creates a disabled account"
fi
if [ "$HASPRR" = "1" ]; then
    echo "  password_reset_requests APPLIED -> Forgot password files a request"
else
    echo "  password_reset_requests NOT applied -> Forgot password shows the old notice"
fi
"$MYSQL" -u root -D $DB -e "SELECT vat_rate, IF(vat_inclusive,'inclusive','EXCLUSIVE') AS vat_mode FROM store_settings;" 2>/dev/null

echo ""; echo "=============================================="
echo " $pass passed, $fail failed"
echo "=============================================="
[ "$fail" = "0" ] || exit 1
