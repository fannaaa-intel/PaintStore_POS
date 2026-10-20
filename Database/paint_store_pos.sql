-- phpMyAdmin SQL Dump
-- version 5.2.1
-- https://www.phpmyadmin.net/
--
-- Host: 127.0.0.1
-- Generation Time: Sep 28, 2026 at 05:14 PM
-- Server version: 10.4.32-MariaDB
-- PHP Version: 8.0.30

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- Database: `paint_store_pos`
--

-- --------------------------------------------------------

--
-- Table structure for table `activity_logs`
--

CREATE TABLE `activity_logs` (
  `log_id` int(11) NOT NULL,
  `user_id` int(11) DEFAULT NULL,
  `action` varchar(50) NOT NULL,
  `entity_type` varchar(40) DEFAULT NULL,
  `entity_id` int(11) DEFAULT NULL,
  `details` varchar(255) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `activity_logs`
--

INSERT INTO `activity_logs` (`log_id`, `user_id`, `action`, `entity_type`, `entity_id`, `details`, `created_at`) VALUES
(1, 2, 'LOGIN', NULL, NULL, NULL, '2026-08-04 22:44:51'),
(2, 2, 'LOGOUT', NULL, NULL, NULL, '2026-08-04 22:44:55'),
(3, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-04 22:53:17'),
(4, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-04 22:56:58'),
(5, 1, 'LOGOUT', NULL, NULL, NULL, '2026-08-04 22:57:05'),
(6, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-04 23:05:52'),
(7, 1, 'USER_RESET_PW', 'user', 2, 'cashier', '2026-08-04 23:07:40'),
(8, 2, 'LOGIN', NULL, NULL, NULL, '2026-08-04 23:09:03'),
(9, 2, 'LOGOUT', NULL, NULL, NULL, '2026-08-04 23:09:07'),
(10, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-04 23:09:11'),
(11, 1, 'USER_CREATE', 'user', 6, 'Created Chanzelyn (Cashier)', '2026-08-04 23:09:35'),
(12, 1, 'LOGOUT', NULL, NULL, NULL, '2026-08-04 23:09:38'),
(13, 6, 'LOGIN', NULL, NULL, NULL, '2026-08-04 23:09:46'),
(14, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-04 23:15:32'),
(15, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-04 23:22:26'),
(16, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-04 23:27:56'),
(17, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-04 23:31:43'),
(18, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-04 23:33:40'),
(19, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-05 09:56:40'),
(20, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-05 10:00:55'),
(21, 1, 'LOGIN', NULL, NULL, NULL, '2026-08-05 10:04:42'),
(22, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-04 21:21:53'),
(23, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-04 21:22:11'),
(24, 4, 'LOGIN', NULL, NULL, NULL, '2026-09-04 21:22:26'),
(25, 4, 'LOGOUT', NULL, NULL, NULL, '2026-09-04 21:22:43'),
(26, 4, 'LOGIN', NULL, NULL, NULL, '2026-09-05 10:46:53'),
(27, 4, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 10:47:30'),
(28, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-05 10:47:35'),
(29, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 10:48:56'),
(30, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-05 10:49:01'),
(31, 5, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 10:49:19'),
(32, 3, 'LOGIN', NULL, NULL, NULL, '2026-09-05 10:49:26'),
(33, 3, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 10:49:46'),
(34, 4, 'LOGIN', NULL, NULL, NULL, '2026-09-05 11:00:09'),
(35, 4, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 11:00:35'),
(36, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-05 11:01:42'),
(37, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-05 11:33:44'),
(38, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-05 11:52:03'),
(39, 5, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 11:58:05'),
(40, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 11:58:14'),
(41, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-05 12:03:22'),
(42, 5, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 12:03:41'),
(43, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 16:40:58'),
(44, 1, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 16:43:39'),
(45, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:05:42'),
(46, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:16:31'),
(47, 1, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 17:17:57'),
(48, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:18:02'),
(49, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 17:18:15'),
(50, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:18:19'),
(51, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:21:13'),
(52, 1, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 17:22:19'),
(53, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:24:57'),
(54, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:28:05'),
(55, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:33:27'),
(56, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:36:28'),
(57, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:39:08'),
(58, 1, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 17:40:13'),
(59, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:40:26'),
(60, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 17:40:37'),
(61, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:40:42'),
(62, 5, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 17:40:51'),
(63, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 17:47:34'),
(64, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 18:01:58'),
(65, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-05 21:24:13'),
(66, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 21:24:49'),
(67, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 21:24:53'),
(68, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 21:47:47'),
(69, 1, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 21:47:54'),
(70, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 21:48:04'),
(71, 1, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 21:48:45'),
(72, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 22:07:09'),
(73, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 22:14:35'),
(74, 1, 'PAYMETHOD_ADD', 'payment_method', 5, 'gcash', '2026-09-05 22:21:25'),
(75, 1, 'PAYMETHOD_DELETE', 'payment_method', 5, 'gcash', '2026-09-05 22:21:35'),
(76, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 22:53:14'),
(77, 1, 'PAYMETHOD_DELETE', 'payment_method', 6, 'GCash', '2026-09-05 22:55:18'),
(78, 1, 'PAYMETHOD_ADD', 'payment_method', 7, 'Maya', '2026-09-05 22:55:36'),
(79, 1, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 22:55:47'),
(80, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-05 22:55:52'),
(81, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-05 22:56:11'),
(82, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 23:03:51'),
(83, 1, 'PAYMETHOD_DELETE', 'payment_method', 7, 'Maya', '2026-09-05 23:05:06'),
(84, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 23:17:37'),
(85, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 23:30:45'),
(86, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 23:39:49'),
(87, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 23:43:12'),
(88, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 23:46:52'),
(89, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-05 23:52:09'),
(90, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 16:46:26'),
(91, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 16:51:31'),
(92, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 17:04:35'),
(93, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 17:13:10'),
(94, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 17:24:34'),
(95, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 17:26:38'),
(96, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 17:31:49'),
(97, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 17:40:09'),
(98, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 17:42:24'),
(99, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 17:49:37'),
(100, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 17:54:17'),
(101, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 18:02:24'),
(102, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-06 18:05:08'),
(103, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-08 00:06:39'),
(104, 5, 'LOGOUT', NULL, NULL, NULL, '2026-09-08 00:06:43'),
(105, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-08 00:35:05'),
(106, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-08 00:35:38'),
(107, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-08 19:36:05'),
(108, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-11 10:22:39'),
(109, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-11 10:23:20'),
(110, 1, 'LOGIN', NULL, NULL, NULL, '2026-09-11 10:23:24'),
(111, 1, 'LOGOUT', NULL, NULL, NULL, '2026-09-11 10:23:39'),
(112, 3, 'LOGIN', NULL, NULL, NULL, '2026-09-11 10:23:47'),
(113, 3, 'LOGOUT', NULL, NULL, NULL, '2026-09-11 10:23:52'),
(114, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-11 10:23:57'),
(115, 5, 'SUPPLIER_EDIT', 'supplier', 5, 'Test Name Name name', '2026-09-11 10:24:17'),
(116, 5, 'LOGOUT', NULL, NULL, NULL, '2026-09-11 10:24:19'),
(117, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-11 10:24:24'),
(118, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-11 10:26:34'),
(119, 5, 'LOGOUT', NULL, NULL, NULL, '2026-09-11 10:26:38'),
(120, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-11 10:26:44'),
(121, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-11 10:27:29'),
(122, 3, 'LOGIN', NULL, NULL, NULL, '2026-09-12 15:40:08'),
(123, 3, 'STOCK_PUTAWAY', 'purchase_order', 1, '1 line, 3 unit(s) shelved, stocked in by Test Inventory (good condition)', '2026-09-12 15:40:58'),
(124, 3, 'LOGOUT', NULL, NULL, NULL, '2026-09-12 15:41:03'),
(125, 3, 'LOGIN', NULL, NULL, NULL, '2026-09-12 15:41:08'),
(126, 3, 'LOGOUT', NULL, NULL, NULL, '2026-09-12 15:41:24'),
(127, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-12 15:41:28'),
(128, 5, 'LOGOUT', NULL, NULL, NULL, '2026-09-12 15:41:37'),
(129, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-15 00:26:59'),
(130, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-15 00:27:04'),
(131, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-15 00:27:18'),
(132, 2, 'SALE_COMPLETE', 'sale', NULL, 'TXN-260915002813-2 — 1 item, ₱600.00 via Cash', '2026-09-15 00:28:13'),
(133, 2, 'SALE_REFUND', 'sale', 33, 'RTN-260915003037-2 against TXN-260915002813-2 — ₱600.00 via Cash (Defective / damaged)', '2026-09-15 00:30:37'),
(134, 2, 'SALE_COMPLETE', 'sale', NULL, 'TXN-260915003105-2 — 1 item, ₱550.00 via Cash', '2026-09-15 00:31:05'),
(135, 2, 'SALE_REFUND', 'sale', 34, 'RTN-260915003242-2 against TXN-260915003105-2 — ₱550.00 via Cash (Defective / damaged)', '2026-09-15 00:32:42'),
(136, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-15 00:45:47'),
(137, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-15 00:51:55'),
(138, 2, 'SHIFT_OPEN', 'shift', 1, 'Register 01', '2026-09-15 00:51:55'),
(139, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-15 00:56:52'),
(140, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-15 01:05:06'),
(141, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-15 01:15:49'),
(142, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-15 01:22:06'),
(143, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-15 01:30:44'),
(144, 2, 'SHIFT_CLOSED_ON_EXIT', NULL, NULL, NULL, '2026-09-15 01:41:30'),
(145, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-15 01:49:59'),
(146, 2, 'SHIFT_OPEN', 'shift', 2, 'Register 01', '2026-09-15 01:49:59'),
(147, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-15 23:36:57'),
(148, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-15 23:39:52'),
(149, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-15 23:41:33'),
(150, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-15 23:47:02'),
(151, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-15 23:50:38'),
(152, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-15 23:52:52'),
(153, 5, 'LOGIN', NULL, NULL, NULL, '2026-09-15 23:56:13'),
(154, 3, 'LOGIN', NULL, NULL, NULL, '2026-09-16 20:32:27'),
(155, 3, 'PRODUCT_EDIT', 'product', 4, 'charcoal', '2026-09-16 20:38:25'),
(156, 3, 'STOCK_ADJUST', 'product', 4, 'Adjustment -1 — Nasira nung delivery', '2026-09-16 20:41:30'),
(157, 3, 'LOGOUT', NULL, NULL, NULL, '2026-09-16 20:45:24'),
(158, 3, 'LOGIN', NULL, NULL, NULL, '2026-09-16 20:53:22'),
(159, 3, 'LOGIN', NULL, NULL, NULL, '2026-09-16 20:57:13'),
(160, 3, 'LOGIN', NULL, NULL, NULL, '2026-09-16 21:01:52'),
(161, 3, 'LOGIN', NULL, NULL, NULL, '2026-09-16 21:09:24'),
(162, 3, 'LOGIN', NULL, NULL, NULL, '2026-09-16 21:11:29'),
(163, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-28 13:02:21'),
(175, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-28 18:02:38'),
(176, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-28 18:02:45'),
(177, 4, 'LOGIN', NULL, NULL, NULL, '2026-09-28 18:03:46'),
(178, 2, 'LOGIN', NULL, NULL, NULL, '2026-09-28 18:12:20'),
(179, 2, 'SHIFT_OPEN', 'shift', 3, 'Register 01, initial cash ₱0.00', '2026-09-28 18:12:40'),
(180, 2, 'LOGOUT', NULL, NULL, NULL, '2026-09-28 18:13:39');

-- --------------------------------------------------------

--
-- Table structure for table `archive`
--

CREATE TABLE `archive` (
  `archive_id` int(11) NOT NULL,
  `source_table` varchar(40) NOT NULL,
  `record_id` int(11) NOT NULL,
  `label` varchar(200) NOT NULL,
  `data_json` longtext DEFAULT NULL,
  `deleted_by` int(11) DEFAULT NULL,
  `deleted_at` datetime NOT NULL DEFAULT current_timestamp(),
  `status` enum('Archived','Restored','Purged') NOT NULL DEFAULT 'Archived',
  `handled_by` int(11) DEFAULT NULL,
  `handled_at` datetime DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `cash_counts`
--

CREATE TABLE `cash_counts` (
  `count_id` int(11) NOT NULL,
  `shift_id` int(11) NOT NULL,
  `denomination` decimal(7,2) NOT NULL,
  `is_bill` tinyint(1) NOT NULL,
  `qty` int(11) NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `cash_counts`
--

INSERT INTO `cash_counts` (`count_id`, `shift_id`, `denomination`, `is_bill`, `qty`) VALUES
(1, 1, 1000.00, 1, 12);

-- --------------------------------------------------------

--
-- Table structure for table `cash_movements`
--

CREATE TABLE `cash_movements` (
  `movement_id` int(11) NOT NULL,
  `shift_id` int(11) NOT NULL,
  `movement_type` enum('PayIn','PayOut','Refund','Drop') NOT NULL,
  `amount` decimal(10,2) NOT NULL,
  `reason` varchar(150) DEFAULT NULL,
  `created_by` int(11) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `categories`
--

CREATE TABLE `categories` (
  `category_id` int(11) NOT NULL,
  `name` varchar(50) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `categories`
--

INSERT INTO `categories` (`category_id`, `name`) VALUES
(4, 'Accessories'),
(1, 'Paint'),
(3, 'Thinner'),
(2, 'Tools');

-- --------------------------------------------------------

--
-- Table structure for table `color_formulas`
--

CREATE TABLE `color_formulas` (
  `formula_id` int(11) NOT NULL,
  `name` varchar(100) NOT NULL,
  `paint_type` varchar(30) NOT NULL,
  `base_product_id` int(11) DEFAULT NULL,
  `size_label` varchar(20) DEFAULT NULL,
  `color_hex` char(7) DEFAULT NULL,
  `notes` varchar(255) DEFAULT NULL,
  `created_by` int(11) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `formula_components`
--

CREATE TABLE `formula_components` (
  `component_id` int(11) NOT NULL,
  `formula_id` int(11) NOT NULL,
  `colorant_name` varchar(50) NOT NULL,
  `amount_ml` decimal(8,2) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `goods_receipts`
--

CREATE TABLE `goods_receipts` (
  `receipt_id` int(11) NOT NULL,
  `po_id` int(11) NOT NULL,
  `phase` enum('receive','putaway') NOT NULL DEFAULT 'receive',
  `received_by` int(11) DEFAULT NULL,
  `receiver_name` varchar(120) NOT NULL,
  `condition_note` enum('Good','Discrepancy') NOT NULL DEFAULT 'Good',
  `remarks` varchar(500) DEFAULT NULL,
  `attested` tinyint(1) NOT NULL DEFAULT 1,
  `total_units` int(11) NOT NULL DEFAULT 0,
  `received_at` datetime NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `goods_receipts`
--

INSERT INTO `goods_receipts` (`receipt_id`, `po_id`, `phase`, `received_by`, `receiver_name`, `condition_note`, `remarks`, `attested`, `total_units`, `received_at`) VALUES
(2, 1, 'putaway', 3, 'Test Inventory', 'Good', 'all goods', 1, 3, '2026-09-12 15:40:58');

-- --------------------------------------------------------

--
-- Table structure for table `goods_receipt_issues`
--

CREATE TABLE `goods_receipt_issues` (
  `issue_id` int(11) NOT NULL,
  `receipt_id` int(11) DEFAULT NULL,
  `po_id` int(11) NOT NULL,
  `po_item_id` int(11) DEFAULT NULL,
  `product_id` int(11) NOT NULL,
  `issue_type` enum('Defective','Missing') NOT NULL,
  `qty` int(11) NOT NULL,
  `details` varchar(300) DEFAULT NULL,
  `action_taken` enum('Return to Supplier','Accept as-is','Awaiting Supplier') NOT NULL,
  `recorded_by` int(11) DEFAULT NULL,
  `recorded_at` datetime NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `password_otp`
--

CREATE TABLE `password_otp` (
  `otp_id` int(11) NOT NULL,
  `user_id` int(11) NOT NULL,
  `code_hash` varchar(255) NOT NULL,
  `expires_at` datetime NOT NULL,
  `attempts` int(11) NOT NULL DEFAULT 0,
  `used_at` datetime DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `password_reset_requests`
--

CREATE TABLE `password_reset_requests` (
  `request_id` int(11) NOT NULL,
  `user_id` int(11) DEFAULT NULL,
  `username_typed` varchar(50) NOT NULL,
  `status` enum('Pending','Handled','Dismissed') NOT NULL DEFAULT 'Pending',
  `requested_at` datetime NOT NULL DEFAULT current_timestamp(),
  `handled_by` int(11) DEFAULT NULL,
  `handled_at` datetime DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `payment_methods`
--

CREATE TABLE `payment_methods` (
  `method_id` int(11) NOT NULL,
  `name` varchar(30) NOT NULL,
  `type` enum('Cash','Reference','Cheque') NOT NULL DEFAULT 'Reference',
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `note` varchar(255) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `payment_methods`
--

INSERT INTO `payment_methods` (`method_id`, `name`, `type`, `is_active`, `note`) VALUES
(1, 'Cash', 'Cash', 1, NULL),
(2, 'Card', 'Reference', 1, NULL),
(3, 'E-Wallet', 'Reference', 1, NULL),
(8, 'Cheque', 'Cheque', 1, NULL);

-- --------------------------------------------------------

--
-- Table structure for table `payment_method_requests`
--

CREATE TABLE `payment_method_requests` (
  `request_id` int(11) NOT NULL,
  `action` enum('Add','Remove') NOT NULL,
  `method_id` int(11) DEFAULT NULL,
  `name` varchar(30) NOT NULL,
  `type` enum('Cash','Reference','Cheque') DEFAULT NULL,
  `note` varchar(255) DEFAULT NULL,
  `reason` varchar(255) NOT NULL,
  `status` enum('Pending','Approved','Rejected') NOT NULL DEFAULT 'Pending',
  `requested_by` int(11) DEFAULT NULL,
  `requested_at` datetime NOT NULL DEFAULT current_timestamp(),
  `reviewed_by` int(11) DEFAULT NULL,
  `reviewed_at` datetime DEFAULT NULL,
  `reject_reason` varchar(255) DEFAULT NULL,
  `outcome` varchar(255) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `po_items`
--

CREATE TABLE `po_items` (
  `po_item_id` int(11) NOT NULL,
  `po_id` int(11) NOT NULL,
  `product_id` int(11) NOT NULL,
  `qty` int(11) NOT NULL,
  `unit_cost` decimal(10,2) NOT NULL,
  `line_cost` decimal(12,2) NOT NULL,
  `request_id` int(11) DEFAULT NULL,
  `shelved_qty` int(11) NOT NULL DEFAULT 0 COMMENT 'Units physically shelved by inventory; incremented by the Put Away screen.'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `po_items`
--

INSERT INTO `po_items` (`po_item_id`, `po_id`, `product_id`, `qty`, `unit_cost`, `line_cost`, `request_id`, `shelved_qty`) VALUES
(1, 1, 7, 10, 999.00, 9990.00, 1, 3);

-- --------------------------------------------------------

--
-- Table structure for table `products`
--

CREATE TABLE `products` (
  `product_id` int(11) NOT NULL,
  `category_id` int(11) NOT NULL,
  `sku` varchar(30) NOT NULL,
  `name` varchar(100) NOT NULL,
  `paint_type` varchar(30) DEFAULT NULL,
  `size_label` varchar(20) DEFAULT NULL,
  `color_hex` char(7) DEFAULT NULL,
  `image_path` varchar(255) DEFAULT NULL,
  `unit_price` decimal(10,2) NOT NULL DEFAULT 0.00,
  `cost_price` decimal(10,2) NOT NULL DEFAULT 0.00,
  `stock_qty` int(11) NOT NULL DEFAULT 0,
  `reorder_level` int(11) NOT NULL DEFAULT 0,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `supplier_id` int(11) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `products`
--

INSERT INTO `products` (`product_id`, `category_id`, `sku`, `name`, `paint_type`, `size_label`, `color_hex`, `image_path`, `unit_price`, `cost_price`, `stock_qty`, `reorder_level`, `is_active`, `created_at`, `supplier_id`) VALUES
(1, 1, 'PNT-0001', 'Ocean Breeze', 'Latex', '5L', '#96CDE1', NULL, 550.00, 0.00, 37, 10, 1, '2026-06-14 12:08:09', NULL),
(2, 1, 'PNT-0002', 'Soft Sage', 'Latex', '4L', '#91B4A0', NULL, 625.00, 0.00, 35, 10, 1, '2026-06-14 12:08:09', NULL),
(3, 1, 'PNT-0003', 'Warm Ivory', 'Latex', '4L', '#EBE6CD', NULL, 700.00, 0.00, 49, 10, 1, '2026-06-14 12:08:09', NULL),
(4, 1, 'PNT-0004', 'charcoal', 'Latex', '5L', '#828791', NULL, 600.00, 0.00, 20, 10, 1, '2026-06-14 12:08:09', NULL),
(5, 1, 'PNT-0005', 'Sunny Yellow', 'Latex', '4L', '#EBDC8C', NULL, 475.00, 0.00, 24, 10, 1, '2026-06-14 12:08:09', NULL),
(6, 1, 'PNT-0006', 'White Primer', 'Latex', '4L', '#FFFFFF', NULL, 350.00, 0.00, 53, 15, 1, '2026-06-14 12:08:09', NULL),
(7, 2, 'TOOL-0001', 'TEST', NULL, 'TEST', NULL, 'C:\\Users\\DELL\\source\\repos\\PaintStore_POS\\ProductImages\\PROD_200a7a95b3aa.png', 999.00, 999.00, 4, 3, 1, '2026-06-21 00:11:06', NULL);

-- --------------------------------------------------------

--
-- Table structure for table `purchase_orders`
--

CREATE TABLE `purchase_orders` (
  `po_id` int(11) NOT NULL,
  `po_no` varchar(20) NOT NULL,
  `supplier_id` int(11) NOT NULL,
  `created_by` int(11) NOT NULL,
  `order_date` date NOT NULL,
  `expected_date` date DEFAULT NULL,
  `status` enum('Draft','Ordered','Partially Received','Received','Cancelled') NOT NULL DEFAULT 'Draft',
  `total_cost` decimal(12,2) NOT NULL DEFAULT 0.00,
  `received_at` datetime DEFAULT NULL,
  `paid_at` datetime DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `purchase_orders`
--

INSERT INTO `purchase_orders` (`po_id`, `po_no`, `supplier_id`, `created_by`, `order_date`, `expected_date`, `status`, `total_cost`, `received_at`, `paid_at`) VALUES
(1, 'PO-260624221846', 5, 5, '2026-06-24', '2026-06-26', 'Received', 9990.00, '2026-06-25 16:50:04', NULL);

-- --------------------------------------------------------

--
-- Table structure for table `purchase_requests`
--

CREATE TABLE `purchase_requests` (
  `request_id` int(11) NOT NULL,
  `product_id` int(11) NOT NULL,
  `requested_by` int(11) NOT NULL,
  `qty_requested` int(11) NOT NULL,
  `status` enum('Pending','Approved','Rejected','Quoted','Converted') NOT NULL DEFAULT 'Pending',
  `note` varchar(150) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `reviewed_by` int(11) DEFAULT NULL,
  `reviewed_at` datetime DEFAULT NULL,
  `review_note` varchar(255) DEFAULT NULL,
  `updated_at` datetime DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `purchase_requests`
--

INSERT INTO `purchase_requests` (`request_id`, `product_id`, `requested_by`, `qty_requested`, `status`, `note`, `created_at`, `reviewed_by`, `reviewed_at`, `review_note`, `updated_at`) VALUES
(1, 7, 3, 5, 'Converted', 'Auto-suggested from Low Stock Alerts. Stock: 1, Reorder level: 3', '2026-06-24 00:27:47', 5, '2026-06-24 22:18:46', NULL, NULL);

-- --------------------------------------------------------

--
-- Table structure for table `purchase_request_items`
--

CREATE TABLE `purchase_request_items` (
  `pr_item_id` int(11) NOT NULL,
  `request_id` int(11) NOT NULL,
  `product_id` int(11) NOT NULL,
  `qty_requested` int(11) NOT NULL DEFAULT 1,
  `note` varchar(200) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `purchase_request_items`
--

INSERT INTO `purchase_request_items` (`pr_item_id`, `request_id`, `product_id`, `qty_requested`, `note`) VALUES
(1, 1, 7, 5, NULL);

-- --------------------------------------------------------

--
-- Table structure for table `quotations`
--

CREATE TABLE `quotations` (
  `quotation_id` int(11) NOT NULL,
  `quotation_no` varchar(20) NOT NULL,
  `request_id` int(11) NOT NULL,
  `supplier_id` int(11) NOT NULL,
  `created_by` int(11) NOT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `valid_until` date DEFAULT NULL,
  `status` enum('Draft','Sent','Accepted','Rejected','Expired') NOT NULL DEFAULT 'Draft',
  `notes` varchar(300) DEFAULT NULL,
  `total_amount` decimal(12,2) NOT NULL DEFAULT 0.00
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `quotation_items`
--

CREATE TABLE `quotation_items` (
  `item_id` int(11) NOT NULL,
  `quotation_id` int(11) NOT NULL,
  `product_id` int(11) NOT NULL,
  `qty_requested` int(11) NOT NULL DEFAULT 1,
  `unit_price` decimal(10,2) NOT NULL DEFAULT 0.00
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `returns`
--

CREATE TABLE `returns` (
  `return_id` int(11) NOT NULL,
  `return_no` varchar(20) NOT NULL,
  `sale_id` int(11) NOT NULL,
  `processed_by` int(11) NOT NULL,
  `return_datetime` datetime NOT NULL DEFAULT current_timestamp(),
  `reason` varchar(100) NOT NULL,
  `refund_method` enum('Cash','CardReversal','EWallet','StoreCredit') NOT NULL,
  `restocking_fee_pct` decimal(5,2) NOT NULL DEFAULT 0.00,
  `total_refund` decimal(10,2) NOT NULL DEFAULT 0.00
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `returns`
--

INSERT INTO `returns` (`return_id`, `return_no`, `sale_id`, `processed_by`, `return_datetime`, `reason`, `refund_method`, `restocking_fee_pct`, `total_refund`) VALUES
(4, 'RTN-260915003037-2', 33, 2, '2026-09-15 00:30:37', 'Defective / damaged', 'Cash', 0.00, 600.00),
(5, 'RTN-260915003242-2', 34, 2, '2026-09-15 00:32:42', 'Defective / damaged', 'Cash', 0.00, 550.00);

-- --------------------------------------------------------

--
-- Table structure for table `return_items`
--

CREATE TABLE `return_items` (
  `return_item_id` int(11) NOT NULL,
  `return_id` int(11) NOT NULL,
  `sale_item_id` int(11) NOT NULL,
  `qty_returned` int(11) NOT NULL,
  `refund_amount` decimal(10,2) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `return_items`
--

INSERT INTO `return_items` (`return_item_id`, `return_id`, `sale_item_id`, `qty_returned`, `refund_amount`) VALUES
(8, 4, 48, 1, 600.00),
(9, 5, 49, 1, 550.00);

-- --------------------------------------------------------

--
-- Table structure for table `roles`
--

CREATE TABLE `roles` (
  `role_id` int(11) NOT NULL,
  `role_name` varchar(30) NOT NULL,
  `description` varchar(150) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `roles`
--

INSERT INTO `roles` (`role_id`, `role_name`, `description`) VALUES
(1, 'Admin', 'Full access to all modules and settings'),
(2, 'Cashier', 'Sales, returns, and cash drawer'),
(3, 'Inventory', 'Stock levels, products, and adjustments'),
(4, 'Accountant', 'Reports, sales, and financial figures'),
(5, 'Procurement', 'Suppliers and purchase orders');

-- --------------------------------------------------------

--
-- Table structure for table `sales`
--

CREATE TABLE `sales` (
  `sale_id` int(11) NOT NULL,
  `sale_no` varchar(20) NOT NULL,
  `user_id` int(11) NOT NULL,
  `shift_id` int(11) DEFAULT NULL,
  `method_id` int(11) DEFAULT NULL,
  `sale_datetime` datetime NOT NULL DEFAULT current_timestamp(),
  `status` varchar(20) NOT NULL DEFAULT 'Completed',
  `discount_type` enum('None','Senior','PWD','Promo') NOT NULL DEFAULT 'None',
  `subtotal` decimal(10,2) NOT NULL DEFAULT 0.00,
  `discount_amount` decimal(10,2) NOT NULL DEFAULT 0.00,
  `vat_amount` decimal(10,2) NOT NULL DEFAULT 0.00,
  `total` decimal(10,2) NOT NULL DEFAULT 0.00,
  `amount_tendered` decimal(10,2) DEFAULT NULL,
  `change_due` decimal(10,2) DEFAULT NULL,
  `reference_no` varchar(64) DEFAULT NULL,
  `cheque_account_name` varchar(120) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `sales`
--

INSERT INTO `sales` (`sale_id`, `sale_no`, `user_id`, `shift_id`, `method_id`, `sale_datetime`, `status`, `discount_type`, `subtotal`, `discount_amount`, `vat_amount`, `total`, `amount_tendered`, `change_due`, `reference_no`, `cheque_account_name`) VALUES
(32, 'TXN-20260623194843', 2, NULL, 1, '2026-06-23 19:48:43', 'Completed', 'None', 6993.00, 0.00, 0.00, 6993.00, 7000.00, 7.00, NULL, NULL),
(33, 'TXN-260915002813-2', 2, NULL, 1, '2026-09-15 00:28:13', 'Refunded', 'None', 600.00, 0.00, 64.29, 600.00, 600.00, 0.00, NULL, NULL),
(34, 'TXN-260915003105-2', 2, NULL, 1, '2026-09-15 00:31:05', 'Refunded', 'None', 550.00, 0.00, 58.93, 550.00, 550.00, 0.00, NULL, NULL);

-- --------------------------------------------------------

--
-- Table structure for table `sale_items`
--

CREATE TABLE `sale_items` (
  `sale_item_id` int(11) NOT NULL,
  `sale_id` int(11) NOT NULL,
  `product_id` int(11) DEFAULT NULL,
  `formula_id` int(11) DEFAULT NULL,
  `description` varchar(120) DEFAULT NULL,
  `qty` int(11) NOT NULL,
  `unit_price` decimal(10,2) NOT NULL,
  `unit_cost` decimal(10,2) NOT NULL DEFAULT 0.00,
  `line_total` decimal(10,2) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `sale_items`
--

INSERT INTO `sale_items` (`sale_item_id`, `sale_id`, `product_id`, `formula_id`, `description`, `qty`, `unit_price`, `unit_cost`, `line_total`) VALUES
(47, 32, 7, NULL, 'TEST', 7, 999.00, 999.00, 6993.00),
(48, 33, 4, NULL, 'Charcoal Gray Ni Mark', 1, 600.00, 0.00, 600.00),
(49, 34, 1, NULL, 'Ocean Breeze', 1, 550.00, 0.00, 550.00);

-- --------------------------------------------------------

--
-- Table structure for table `shifts`
--

CREATE TABLE `shifts` (
  `shift_id` int(11) NOT NULL,
  `user_id` int(11) NOT NULL,
  `register_no` varchar(10) NOT NULL DEFAULT '01',
  `opening_float` decimal(10,2) NOT NULL DEFAULT 0.00,
  `expected_opening` decimal(10,2) DEFAULT NULL,
  `opening_variance` decimal(10,2) DEFAULT NULL,
  `started_at` datetime NOT NULL DEFAULT current_timestamp(),
  `ended_at` datetime DEFAULT NULL,
  `expected_cash` decimal(10,2) DEFAULT NULL,
  `counted_cash` decimal(10,2) DEFAULT NULL,
  `variance` decimal(10,2) DEFAULT NULL,
  `status` enum('Open','Closed') NOT NULL DEFAULT 'Open'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `shifts`
--

INSERT INTO `shifts` (`shift_id`, `user_id`, `register_no`, `opening_float`, `expected_opening`, `opening_variance`, `started_at`, `ended_at`, `expected_cash`, `counted_cash`, `variance`, `status`) VALUES
(1, 2, '01', 0.00, NULL, NULL, '2026-09-15 00:51:55', '2026-09-15 01:41:30', NULL, NULL, NULL, 'Closed'),
(2, 2, '01', 0.00, NULL, NULL, '2026-09-15 01:49:59', '2026-09-28 18:02:45', NULL, NULL, NULL, 'Closed'),
(3, 2, '01', 0.00, NULL, NULL, '2026-09-28 18:12:40', '2026-09-28 18:13:39', NULL, NULL, NULL, 'Closed');

-- --------------------------------------------------------

--
-- Table structure for table `stock_movements`
--

CREATE TABLE `stock_movements` (
  `movement_id` int(11) NOT NULL,
  `product_id` int(11) NOT NULL,
  `user_id` int(11) DEFAULT NULL,
  `movement_type` enum('Initial','Sale','Return','Purchase','Adjustment') NOT NULL,
  `qty_change` int(11) NOT NULL,
  `reference_type` varchar(20) DEFAULT NULL,
  `reference_id` int(11) DEFAULT NULL,
  `note` varchar(150) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `stock_movements`
--

INSERT INTO `stock_movements` (`movement_id`, `product_id`, `user_id`, `movement_type`, `qty_change`, `reference_type`, `reference_id`, `note`, `created_at`) VALUES
(41, 7, 3, 'Purchase', 5, 'receive', NULL, 'New Arrival', '2026-06-22 00:34:13'),
(42, 7, 2, 'Sale', -7, NULL, NULL, NULL, '2026-06-23 19:48:43'),
(43, 7, 5, 'Purchase', 10, 'receive', 1, 'Received via PO PO-260624221846', '2026-06-25 16:50:04'),
(44, 7, 3, 'Purchase', 3, 'putaway', 1, 'Stocked in from PO PO-260624221846', '2026-09-12 15:40:58'),
(45, 4, 2, 'Sale', -1, NULL, NULL, NULL, '2026-09-15 00:28:13'),
(46, 4, 2, 'Adjustment', 1, 'return', 4, 'Return of sale', '2026-09-15 00:30:37'),
(47, 1, 2, 'Sale', -1, NULL, NULL, NULL, '2026-09-15 00:31:05'),
(48, 1, 2, 'Adjustment', 1, 'return', 5, 'Return of sale', '2026-09-15 00:32:42'),
(49, 4, 3, 'Adjustment', -1, 'damage', NULL, 'Nasira nung delivery', '2026-09-16 20:41:30');

-- --------------------------------------------------------

--
-- Table structure for table `store_settings`
--

CREATE TABLE `store_settings` (
  `setting_id` int(11) NOT NULL DEFAULT 1,
  `store_name` varchar(100) NOT NULL DEFAULT 'PAINT STORE POS',
  `store_tagline` varchar(100) NOT NULL DEFAULT 'Sales Receipt',
  `store_address` varchar(255) DEFAULT NULL,
  `store_contact` varchar(100) DEFAULT NULL,
  `store_tin` varchar(50) DEFAULT NULL,
  `vat_rate` decimal(5,2) NOT NULL DEFAULT 12.00,
  `vat_inclusive` tinyint(1) NOT NULL DEFAULT 1,
  `updated_at` datetime NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `sig_checked_name` varchar(100) DEFAULT NULL,
  `sig_checked_position` varchar(100) DEFAULT NULL,
  `sig_approved_name` varchar(100) DEFAULT NULL,
  `sig_approved_position` varchar(100) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `store_settings`
--

INSERT INTO `store_settings` (`setting_id`, `store_name`, `store_tagline`, `store_address`, `store_contact`, `store_tin`, `vat_rate`, `vat_inclusive`, `updated_at`, `sig_checked_name`, `sig_checked_position`, `sig_approved_name`, `sig_approved_position`) VALUES
(1, 'PAINT STORE POS', 'Sales Receipt', NULL, NULL, NULL, 12.00, 1, '2026-09-28 17:55:11', '', '', '', '');

-- --------------------------------------------------------

--
-- Table structure for table `suppliers`
--

CREATE TABLE `suppliers` (
  `supplier_id` int(11) NOT NULL,
  `name` varchar(100) NOT NULL,
  `company_name` varchar(150) DEFAULT NULL,
  `contact_person` varchar(100) DEFAULT NULL,
  `head_name` varchar(120) DEFAULT NULL,
  `phone` varchar(30) DEFAULT NULL,
  `email` varchar(100) DEFAULT NULL,
  `tin` varchar(30) DEFAULT NULL,
  `dti_no` varchar(60) DEFAULT NULL,
  `bir_no` varchar(60) DEFAULT NULL,
  `address` varchar(200) DEFAULT NULL,
  `payment_terms` varchar(40) DEFAULT NULL,
  `notes` varchar(500) DEFAULT NULL,
  `image_path` varchar(255) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `suppliers`
--

INSERT INTO `suppliers` (`supplier_id`, `name`, `company_name`, `contact_person`, `head_name`, `phone`, `email`, `tin`, `dti_no`, `bir_no`, `address`, `payment_terms`, `notes`, `image_path`, `is_active`) VALUES
(5, 'Test Name Name name', NULL, 'Test Name Name name', NULL, '095106946647', 'Test@gmail.com', NULL, NULL, NULL, 'Macanaya, Aparri', NULL, NULL, 'SupplierImages\\SUP_e288fb1affad.png', 1),
(6, 'Valorant', NULL, 'Juan Dela Cruz', NULL, '12345', 'Valorant@gmail.com', NULL, NULL, NULL, 'Punta Cagayan', NULL, NULL, NULL, 1);

-- --------------------------------------------------------

--
-- Table structure for table `supplier_payments`
--

CREATE TABLE `supplier_payments` (
  `payment_id` int(11) NOT NULL,
  `po_id` int(11) NOT NULL,
  `amount_paid` decimal(12,2) NOT NULL,
  `po_total` decimal(12,2) NOT NULL DEFAULT 0.00,
  `reference_no` varchar(60) DEFAULT NULL,
  `method` enum('Payment on Delivery','Cash','Bank Transfer','Cheque','E-Wallet') NOT NULL DEFAULT 'Payment on Delivery',
  `paid_on` date NOT NULL,
  `remarks` varchar(300) DEFAULT NULL,
  `recorded_by` int(11) DEFAULT NULL,
  `recorded_at` datetime NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `users`
--

CREATE TABLE `users` (
  `user_id` int(11) NOT NULL,
  `role_id` int(11) NOT NULL,
  `full_name` varchar(100) NOT NULL,
  `username` varchar(50) NOT NULL,
  `email` varchar(120) DEFAULT NULL,
  `password_hash` varchar(255) NOT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `status` enum('Pending','Approved','Rejected') NOT NULL DEFAULT 'Approved',
  `requested_role_id` int(11) DEFAULT NULL,
  `reviewed_by` int(11) DEFAULT NULL,
  `reviewed_at` datetime DEFAULT NULL,
  `reject_reason` varchar(255) DEFAULT NULL,
  `must_change_password` tinyint(1) NOT NULL DEFAULT 0,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `sales_quota` decimal(10,2) NOT NULL DEFAULT 0.00
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Dumping data for table `users`
--

INSERT INTO `users` (`user_id`, `role_id`, `full_name`, `username`, `email`, `password_hash`, `is_active`, `status`, `requested_role_id`, `reviewed_by`, `reviewed_at`, `reject_reason`, `must_change_password`, `created_at`, `sales_quota`) VALUES
(1, 1, 'System Administrator', 'admin', NULL, '100000:KkImNeukCua9+lFhfFYdLQ==:1Fi3J9rz3+ZyIlIXLsXeBuFbvzOcyT9hWiAHRwvIE3o=', 1, 'Approved', NULL, NULL, NULL, NULL, 0, '2026-06-14 12:08:09', 0.00),
(2, 2, 'Test Cashier', 'cashier', NULL, '100000:MplcoBlF+pI15psscpbSHQ==:4uYH1lmsHLyuILwYXpOXtkVmLgBeqKoit41MGz4oAnc=', 1, 'Approved', NULL, NULL, NULL, NULL, 0, '2026-06-14 12:08:09', 0.00),
(3, 3, 'Test Inventory', 'inventory', NULL, '100000:A1d7XpQrWiIh63F8i6bL0A==:hcEp/yP6gV4Lyovg0YDMgM1zEED2T0Dv/k7TsxQ1gCI=', 1, 'Approved', NULL, NULL, NULL, NULL, 0, '2026-06-14 12:08:09', 0.00),
(4, 4, 'Test Accountant', 'accountant', NULL, '100000:yvm3MYbrzXrYctmCnxR4Kw==:diIZIDrbZMnYoO8/WjuaSrFvRy1OBkX/d4VM1neGwcU=', 1, 'Approved', NULL, NULL, NULL, NULL, 0, '2026-06-14 12:08:09', 0.00),
(5, 5, 'Test Procurement', 'procurement', NULL, '100000:ieS/j7nLWOJoO42LH8aITw==:bLjrEm755GnRSxjWp7nAKQExHb338VSN8gMrOQB4bl0=', 1, 'Approved', NULL, NULL, NULL, NULL, 0, '2026-06-14 12:08:09', 0.00),
(6, 2, 'Chang', 'Chanzelyn', NULL, '100000:hIwAoRKHiLGEo481ek7H3g==:dfohprxUqXJtHIlRhj+uJF8WaQxBhRQjKrS5+pTbYnk=', 1, 'Approved', NULL, NULL, NULL, NULL, 0, '2026-08-04 23:09:35', 5000.00);

--
-- Indexes for dumped tables
--

--
-- Indexes for table `activity_logs`
--
ALTER TABLE `activity_logs`
  ADD PRIMARY KEY (`log_id`),
  ADD KEY `fk_activity_user` (`user_id`),
  ADD KEY `idx_activity_created` (`created_at`);

--
-- Indexes for table `archive`
--
ALTER TABLE `archive`
  ADD PRIMARY KEY (`archive_id`),
  ADD KEY `idx_archive_status` (`status`),
  ADD KEY `idx_archive_source` (`source_table`,`record_id`);

--
-- Indexes for table `cash_counts`
--
ALTER TABLE `cash_counts`
  ADD PRIMARY KEY (`count_id`),
  ADD UNIQUE KEY `uq_count` (`shift_id`,`denomination`,`is_bill`);

--
-- Indexes for table `cash_movements`
--
ALTER TABLE `cash_movements`
  ADD PRIMARY KEY (`movement_id`),
  ADD KEY `fk_cashmove_shift` (`shift_id`),
  ADD KEY `fk_cashmove_user` (`created_by`);

--
-- Indexes for table `categories`
--
ALTER TABLE `categories`
  ADD PRIMARY KEY (`category_id`),
  ADD UNIQUE KEY `name` (`name`);

--
-- Indexes for table `color_formulas`
--
ALTER TABLE `color_formulas`
  ADD PRIMARY KEY (`formula_id`),
  ADD KEY `fk_formula_base` (`base_product_id`),
  ADD KEY `fk_formula_user` (`created_by`);

--
-- Indexes for table `formula_components`
--
ALTER TABLE `formula_components`
  ADD PRIMARY KEY (`component_id`),
  ADD KEY `fk_component_formula` (`formula_id`);

--
-- Indexes for table `goods_receipts`
--
ALTER TABLE `goods_receipts`
  ADD PRIMARY KEY (`receipt_id`),
  ADD KEY `idx_gr_po` (`po_id`),
  ADD KEY `idx_gr_user` (`received_by`),
  ADD KEY `idx_gr_po_phase` (`po_id`,`phase`);

--
-- Indexes for table `goods_receipt_issues`
--
ALTER TABLE `goods_receipt_issues`
  ADD PRIMARY KEY (`issue_id`),
  ADD KEY `idx_gri_po` (`po_id`),
  ADD KEY `idx_gri_receipt` (`receipt_id`),
  ADD KEY `idx_gri_product` (`product_id`);

--
-- Indexes for table `password_otp`
--
ALTER TABLE `password_otp`
  ADD PRIMARY KEY (`otp_id`),
  ADD KEY `idx_otp_user` (`user_id`);

--
-- Indexes for table `password_reset_requests`
--
ALTER TABLE `password_reset_requests`
  ADD PRIMARY KEY (`request_id`),
  ADD KEY `idx_prr_status` (`status`),
  ADD KEY `idx_prr_user` (`user_id`);

--
-- Indexes for table `payment_methods`
--
ALTER TABLE `payment_methods`
  ADD PRIMARY KEY (`method_id`),
  ADD UNIQUE KEY `name` (`name`);

--
-- Indexes for table `payment_method_requests`
--
ALTER TABLE `payment_method_requests`
  ADD PRIMARY KEY (`request_id`),
  ADD KEY `idx_pmr_status` (`status`),
  ADD KEY `idx_pmr_user` (`requested_by`);

--
-- Indexes for table `po_items`
--
ALTER TABLE `po_items`
  ADD PRIMARY KEY (`po_item_id`),
  ADD KEY `fk_poitems_po` (`po_id`),
  ADD KEY `fk_poitems_product` (`product_id`),
  ADD KEY `idx_poi_putaway` (`po_id`,`shelved_qty`);

--
-- Indexes for table `products`
--
ALTER TABLE `products`
  ADD PRIMARY KEY (`product_id`),
  ADD UNIQUE KEY `sku` (`sku`),
  ADD KEY `fk_products_category` (`category_id`),
  ADD KEY `idx_products_active` (`is_active`),
  ADD KEY `fk_product_supplier` (`supplier_id`);

--
-- Indexes for table `purchase_orders`
--
ALTER TABLE `purchase_orders`
  ADD PRIMARY KEY (`po_id`),
  ADD UNIQUE KEY `po_no` (`po_no`),
  ADD KEY `fk_po_supplier` (`supplier_id`),
  ADD KEY `fk_po_user` (`created_by`);

--
-- Indexes for table `purchase_requests`
--
ALTER TABLE `purchase_requests`
  ADD PRIMARY KEY (`request_id`),
  ADD KEY `fk_pr_product` (`product_id`),
  ADD KEY `fk_pr_requester` (`requested_by`);

--
-- Indexes for table `purchase_request_items`
--
ALTER TABLE `purchase_request_items`
  ADD PRIMARY KEY (`pr_item_id`),
  ADD KEY `idx_pri_request` (`request_id`),
  ADD KEY `idx_pri_product` (`product_id`);

--
-- Indexes for table `quotations`
--
ALTER TABLE `quotations`
  ADD PRIMARY KEY (`quotation_id`),
  ADD UNIQUE KEY `uq_quotation_no` (`quotation_no`),
  ADD KEY `fk_quot_request` (`request_id`),
  ADD KEY `fk_quot_supplier` (`supplier_id`),
  ADD KEY `fk_quot_user` (`created_by`);

--
-- Indexes for table `quotation_items`
--
ALTER TABLE `quotation_items`
  ADD PRIMARY KEY (`item_id`),
  ADD KEY `fk_qitem_quotation` (`quotation_id`),
  ADD KEY `fk_qitem_product` (`product_id`);

--
-- Indexes for table `returns`
--
ALTER TABLE `returns`
  ADD PRIMARY KEY (`return_id`),
  ADD UNIQUE KEY `return_no` (`return_no`),
  ADD KEY `fk_returns_sale` (`sale_id`),
  ADD KEY `fk_returns_user` (`processed_by`);

--
-- Indexes for table `return_items`
--
ALTER TABLE `return_items`
  ADD PRIMARY KEY (`return_item_id`),
  ADD KEY `fk_retitems_return` (`return_id`),
  ADD KEY `fk_retitems_saleitem` (`sale_item_id`);

--
-- Indexes for table `roles`
--
ALTER TABLE `roles`
  ADD PRIMARY KEY (`role_id`),
  ADD UNIQUE KEY `role_name` (`role_name`);

--
-- Indexes for table `sales`
--
ALTER TABLE `sales`
  ADD PRIMARY KEY (`sale_id`),
  ADD UNIQUE KEY `sale_no` (`sale_no`),
  ADD KEY `fk_sales_user` (`user_id`),
  ADD KEY `fk_sales_shift` (`shift_id`),
  ADD KEY `fk_sales_method` (`method_id`),
  ADD KEY `idx_sales_datetime` (`sale_datetime`),
  ADD KEY `idx_sales_status` (`status`),
  ADD KEY `idx_sales_reference_no` (`reference_no`);

--
-- Indexes for table `sale_items`
--
ALTER TABLE `sale_items`
  ADD PRIMARY KEY (`sale_item_id`),
  ADD KEY `fk_items_product` (`product_id`),
  ADD KEY `fk_items_formula` (`formula_id`),
  ADD KEY `idx_saleitems_sale` (`sale_id`);

--
-- Indexes for table `shifts`
--
ALTER TABLE `shifts`
  ADD PRIMARY KEY (`shift_id`),
  ADD KEY `fk_shifts_user` (`user_id`);

--
-- Indexes for table `stock_movements`
--
ALTER TABLE `stock_movements`
  ADD PRIMARY KEY (`movement_id`),
  ADD KEY `fk_stock_user` (`user_id`),
  ADD KEY `idx_stock_product` (`product_id`);

--
-- Indexes for table `store_settings`
--
ALTER TABLE `store_settings`
  ADD PRIMARY KEY (`setting_id`);

--
-- Indexes for table `suppliers`
--
ALTER TABLE `suppliers`
  ADD PRIMARY KEY (`supplier_id`);

--
-- Indexes for table `supplier_payments`
--
ALTER TABLE `supplier_payments`
  ADD PRIMARY KEY (`payment_id`),
  ADD KEY `idx_sp_po` (`po_id`),
  ADD KEY `idx_sp_paid_on` (`paid_on`),
  ADD KEY `idx_sp_user` (`recorded_by`);

--
-- Indexes for table `users`
--
ALTER TABLE `users`
  ADD PRIMARY KEY (`user_id`),
  ADD UNIQUE KEY `username` (`username`),
  ADD UNIQUE KEY `uq_users_email` (`email`),
  ADD KEY `fk_users_role` (`role_id`);

--
-- AUTO_INCREMENT for dumped tables
--

--
-- AUTO_INCREMENT for table `activity_logs`
--
ALTER TABLE `activity_logs`
  MODIFY `log_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=181;

--
-- AUTO_INCREMENT for table `archive`
--
ALTER TABLE `archive`
  MODIFY `archive_id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `cash_counts`
--
ALTER TABLE `cash_counts`
  MODIFY `count_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=3;

--
-- AUTO_INCREMENT for table `cash_movements`
--
ALTER TABLE `cash_movements`
  MODIFY `movement_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=4;

--
-- AUTO_INCREMENT for table `categories`
--
ALTER TABLE `categories`
  MODIFY `category_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=5;

--
-- AUTO_INCREMENT for table `color_formulas`
--
ALTER TABLE `color_formulas`
  MODIFY `formula_id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `formula_components`
--
ALTER TABLE `formula_components`
  MODIFY `component_id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `goods_receipts`
--
ALTER TABLE `goods_receipts`
  MODIFY `receipt_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=3;

--
-- AUTO_INCREMENT for table `goods_receipt_issues`
--
ALTER TABLE `goods_receipt_issues`
  MODIFY `issue_id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `password_otp`
--
ALTER TABLE `password_otp`
  MODIFY `otp_id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `password_reset_requests`
--
ALTER TABLE `password_reset_requests`
  MODIFY `request_id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `payment_methods`
--
ALTER TABLE `payment_methods`
  MODIFY `method_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=9;

--
-- AUTO_INCREMENT for table `payment_method_requests`
--
ALTER TABLE `payment_method_requests`
  MODIFY `request_id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `po_items`
--
ALTER TABLE `po_items`
  MODIFY `po_item_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=2;

--
-- AUTO_INCREMENT for table `products`
--
ALTER TABLE `products`
  MODIFY `product_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=8;

--
-- AUTO_INCREMENT for table `purchase_orders`
--
ALTER TABLE `purchase_orders`
  MODIFY `po_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=2;

--
-- AUTO_INCREMENT for table `purchase_requests`
--
ALTER TABLE `purchase_requests`
  MODIFY `request_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=5;

--
-- AUTO_INCREMENT for table `purchase_request_items`
--
ALTER TABLE `purchase_request_items`
  MODIFY `pr_item_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=8;

--
-- AUTO_INCREMENT for table `quotations`
--
ALTER TABLE `quotations`
  MODIFY `quotation_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=5;

--
-- AUTO_INCREMENT for table `quotation_items`
--
ALTER TABLE `quotation_items`
  MODIFY `item_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=11;

--
-- AUTO_INCREMENT for table `returns`
--
ALTER TABLE `returns`
  MODIFY `return_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=6;

--
-- AUTO_INCREMENT for table `return_items`
--
ALTER TABLE `return_items`
  MODIFY `return_item_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=10;

--
-- AUTO_INCREMENT for table `roles`
--
ALTER TABLE `roles`
  MODIFY `role_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=6;

--
-- AUTO_INCREMENT for table `sales`
--
ALTER TABLE `sales`
  MODIFY `sale_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=35;

--
-- AUTO_INCREMENT for table `sale_items`
--
ALTER TABLE `sale_items`
  MODIFY `sale_item_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=50;

--
-- AUTO_INCREMENT for table `shifts`
--
ALTER TABLE `shifts`
  MODIFY `shift_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=5;

--
-- AUTO_INCREMENT for table `stock_movements`
--
ALTER TABLE `stock_movements`
  MODIFY `movement_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=50;

--
-- AUTO_INCREMENT for table `suppliers`
--
ALTER TABLE `suppliers`
  MODIFY `supplier_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=8;

--
-- AUTO_INCREMENT for table `supplier_payments`
--
ALTER TABLE `supplier_payments`
  MODIFY `payment_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=3;

--
-- AUTO_INCREMENT for table `users`
--
ALTER TABLE `users`
  MODIFY `user_id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=13;

--
-- Constraints for dumped tables
--

--
-- Constraints for table `activity_logs`
--
ALTER TABLE `activity_logs`
  ADD CONSTRAINT `fk_activity_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL;

--
-- Constraints for table `cash_counts`
--
ALTER TABLE `cash_counts`
  ADD CONSTRAINT `fk_counts_shift` FOREIGN KEY (`shift_id`) REFERENCES `shifts` (`shift_id`) ON DELETE CASCADE ON UPDATE CASCADE;

--
-- Constraints for table `cash_movements`
--
ALTER TABLE `cash_movements`
  ADD CONSTRAINT `fk_cashmove_shift` FOREIGN KEY (`shift_id`) REFERENCES `shifts` (`shift_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_cashmove_user` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL ON UPDATE CASCADE;

--
-- Constraints for table `color_formulas`
--
ALTER TABLE `color_formulas`
  ADD CONSTRAINT `fk_formula_base` FOREIGN KEY (`base_product_id`) REFERENCES `products` (`product_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_formula_user` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL ON UPDATE CASCADE;

--
-- Constraints for table `formula_components`
--
ALTER TABLE `formula_components`
  ADD CONSTRAINT `fk_component_formula` FOREIGN KEY (`formula_id`) REFERENCES `color_formulas` (`formula_id`) ON DELETE CASCADE ON UPDATE CASCADE;

--
-- Constraints for table `po_items`
--
ALTER TABLE `po_items`
  ADD CONSTRAINT `fk_poitems_po` FOREIGN KEY (`po_id`) REFERENCES `purchase_orders` (`po_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_poitems_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`product_id`) ON UPDATE CASCADE;

--
-- Constraints for table `products`
--
ALTER TABLE `products`
  ADD CONSTRAINT `fk_products_category` FOREIGN KEY (`category_id`) REFERENCES `categories` (`category_id`) ON UPDATE CASCADE;

--
-- Constraints for table `purchase_orders`
--
ALTER TABLE `purchase_orders`
  ADD CONSTRAINT `fk_po_supplier` FOREIGN KEY (`supplier_id`) REFERENCES `suppliers` (`supplier_id`) ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_po_user` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`) ON UPDATE CASCADE;

--
-- Constraints for table `purchase_request_items`
--
ALTER TABLE `purchase_request_items`
  ADD CONSTRAINT `fk_pri_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`product_id`),
  ADD CONSTRAINT `fk_pri_request` FOREIGN KEY (`request_id`) REFERENCES `purchase_requests` (`request_id`) ON DELETE CASCADE;

--
-- Constraints for table `quotations`
--
ALTER TABLE `quotations`
  ADD CONSTRAINT `fk_quot_request` FOREIGN KEY (`request_id`) REFERENCES `purchase_requests` (`request_id`),
  ADD CONSTRAINT `fk_quot_supplier` FOREIGN KEY (`supplier_id`) REFERENCES `suppliers` (`supplier_id`),
  ADD CONSTRAINT `fk_quot_user` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`);

--
-- Constraints for table `quotation_items`
--
ALTER TABLE `quotation_items`
  ADD CONSTRAINT `fk_qitem_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`product_id`),
  ADD CONSTRAINT `fk_qitem_quotation` FOREIGN KEY (`quotation_id`) REFERENCES `quotations` (`quotation_id`) ON DELETE CASCADE;

--
-- Constraints for table `returns`
--
ALTER TABLE `returns`
  ADD CONSTRAINT `fk_returns_sale` FOREIGN KEY (`sale_id`) REFERENCES `sales` (`sale_id`) ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_returns_user` FOREIGN KEY (`processed_by`) REFERENCES `users` (`user_id`) ON UPDATE CASCADE;

--
-- Constraints for table `return_items`
--
ALTER TABLE `return_items`
  ADD CONSTRAINT `fk_retitems_return` FOREIGN KEY (`return_id`) REFERENCES `returns` (`return_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_retitems_saleitem` FOREIGN KEY (`sale_item_id`) REFERENCES `sale_items` (`sale_item_id`) ON UPDATE CASCADE;

--
-- Constraints for table `sales`
--
ALTER TABLE `sales`
  ADD CONSTRAINT `fk_sales_method` FOREIGN KEY (`method_id`) REFERENCES `payment_methods` (`method_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_sales_shift` FOREIGN KEY (`shift_id`) REFERENCES `shifts` (`shift_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_sales_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON UPDATE CASCADE;

--
-- Constraints for table `sale_items`
--
ALTER TABLE `sale_items`
  ADD CONSTRAINT `fk_items_formula` FOREIGN KEY (`formula_id`) REFERENCES `color_formulas` (`formula_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_items_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`product_id`) ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_items_sale` FOREIGN KEY (`sale_id`) REFERENCES `sales` (`sale_id`) ON DELETE CASCADE ON UPDATE CASCADE;

--
-- Constraints for table `shifts`
--
ALTER TABLE `shifts`
  ADD CONSTRAINT `fk_shifts_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON UPDATE CASCADE;

--
-- Constraints for table `stock_movements`
--
ALTER TABLE `stock_movements`
  ADD CONSTRAINT `fk_stock_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`product_id`) ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_stock_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL ON UPDATE CASCADE;

--
-- Constraints for table `users`
--
ALTER TABLE `users`
  ADD CONSTRAINT `fk_users_role` FOREIGN KEY (`role_id`) REFERENCES `roles` (`role_id`) ON UPDATE CASCADE;
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
