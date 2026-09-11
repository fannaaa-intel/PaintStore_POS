-- MariaDB dump 10.19  Distrib 10.4.32-MariaDB, for Win64 (AMD64)
--
-- Host: localhost    Database: paint_store_pos
-- ------------------------------------------------------
-- Server version	10.4.32-MariaDB

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

--
-- Current Database: `paint_store_pos`
--

/*!40000 DROP DATABASE IF EXISTS `paint_store_pos`*/;

CREATE DATABASE /*!32312 IF NOT EXISTS*/ `paint_store_pos` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci */;

USE `paint_store_pos`;

--
-- Table structure for table `activity_logs`
--

DROP TABLE IF EXISTS `activity_logs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `activity_logs` (
  `log_id` int(11) NOT NULL AUTO_INCREMENT,
  `user_id` int(11) DEFAULT NULL,
  `action` varchar(50) NOT NULL,
  `entity_type` varchar(40) DEFAULT NULL,
  `entity_id` int(11) DEFAULT NULL,
  `details` varchar(255) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`log_id`),
  KEY `fk_activity_user` (`user_id`),
  KEY `idx_activity_created` (`created_at`),
  CONSTRAINT `fk_activity_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL
) ENGINE=InnoDB AUTO_INCREMENT=120 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `activity_logs`
--

LOCK TABLES `activity_logs` WRITE;
/*!40000 ALTER TABLE `activity_logs` DISABLE KEYS */;
INSERT INTO `activity_logs` VALUES (1,2,'LOGIN',NULL,NULL,NULL,'2026-08-04 22:44:51'),(2,2,'LOGOUT',NULL,NULL,NULL,'2026-08-04 22:44:55'),(3,1,'LOGIN',NULL,NULL,NULL,'2026-08-04 22:53:17'),(4,1,'LOGIN',NULL,NULL,NULL,'2026-08-04 22:56:58'),(5,1,'LOGOUT',NULL,NULL,NULL,'2026-08-04 22:57:05'),(6,1,'LOGIN',NULL,NULL,NULL,'2026-08-04 23:05:52'),(7,1,'USER_RESET_PW','user',2,'cashier','2026-08-04 23:07:40'),(8,2,'LOGIN',NULL,NULL,NULL,'2026-08-04 23:09:03'),(9,2,'LOGOUT',NULL,NULL,NULL,'2026-08-04 23:09:07'),(10,1,'LOGIN',NULL,NULL,NULL,'2026-08-04 23:09:11'),(11,1,'USER_CREATE','user',6,'Created Chanzelyn (Cashier)','2026-08-04 23:09:35'),(12,1,'LOGOUT',NULL,NULL,NULL,'2026-08-04 23:09:38'),(13,6,'LOGIN',NULL,NULL,NULL,'2026-08-04 23:09:46'),(14,1,'LOGIN',NULL,NULL,NULL,'2026-08-04 23:15:32'),(15,1,'LOGIN',NULL,NULL,NULL,'2026-08-04 23:22:26'),(16,1,'LOGIN',NULL,NULL,NULL,'2026-08-04 23:27:56'),(17,1,'LOGIN',NULL,NULL,NULL,'2026-08-04 23:31:43'),(18,1,'LOGIN',NULL,NULL,NULL,'2026-08-04 23:33:40'),(19,1,'LOGIN',NULL,NULL,NULL,'2026-08-05 09:56:40'),(20,1,'LOGIN',NULL,NULL,NULL,'2026-08-05 10:00:55'),(21,1,'LOGIN',NULL,NULL,NULL,'2026-08-05 10:04:42'),(22,2,'LOGIN',NULL,NULL,NULL,'2026-09-04 21:21:53'),(23,2,'LOGOUT',NULL,NULL,NULL,'2026-09-04 21:22:11'),(24,4,'LOGIN',NULL,NULL,NULL,'2026-09-04 21:22:26'),(25,4,'LOGOUT',NULL,NULL,NULL,'2026-09-04 21:22:43'),(26,4,'LOGIN',NULL,NULL,NULL,'2026-09-05 10:46:53'),(27,4,'LOGOUT',NULL,NULL,NULL,'2026-09-05 10:47:30'),(28,2,'LOGIN',NULL,NULL,NULL,'2026-09-05 10:47:35'),(29,2,'LOGOUT',NULL,NULL,NULL,'2026-09-05 10:48:56'),(30,5,'LOGIN',NULL,NULL,NULL,'2026-09-05 10:49:01'),(31,5,'LOGOUT',NULL,NULL,NULL,'2026-09-05 10:49:19'),(32,3,'LOGIN',NULL,NULL,NULL,'2026-09-05 10:49:26'),(33,3,'LOGOUT',NULL,NULL,NULL,'2026-09-05 10:49:46'),(34,4,'LOGIN',NULL,NULL,NULL,'2026-09-05 11:00:09'),(35,4,'LOGOUT',NULL,NULL,NULL,'2026-09-05 11:00:35'),(36,5,'LOGIN',NULL,NULL,NULL,'2026-09-05 11:01:42'),(37,5,'LOGIN',NULL,NULL,NULL,'2026-09-05 11:33:44'),(38,5,'LOGIN',NULL,NULL,NULL,'2026-09-05 11:52:03'),(39,5,'LOGOUT',NULL,NULL,NULL,'2026-09-05 11:58:05'),(40,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 11:58:14'),(41,5,'LOGIN',NULL,NULL,NULL,'2026-09-05 12:03:22'),(42,5,'LOGOUT',NULL,NULL,NULL,'2026-09-05 12:03:41'),(43,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 16:40:58'),(44,1,'LOGOUT',NULL,NULL,NULL,'2026-09-05 16:43:39'),(45,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:05:42'),(46,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:16:31'),(47,1,'LOGOUT',NULL,NULL,NULL,'2026-09-05 17:17:57'),(48,2,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:18:02'),(49,2,'LOGOUT',NULL,NULL,NULL,'2026-09-05 17:18:15'),(50,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:18:19'),(51,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:21:13'),(52,1,'LOGOUT',NULL,NULL,NULL,'2026-09-05 17:22:19'),(53,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:24:57'),(54,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:28:05'),(55,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:33:27'),(56,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:36:28'),(57,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:39:08'),(58,1,'LOGOUT',NULL,NULL,NULL,'2026-09-05 17:40:13'),(59,2,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:40:26'),(60,2,'LOGOUT',NULL,NULL,NULL,'2026-09-05 17:40:37'),(61,5,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:40:42'),(62,5,'LOGOUT',NULL,NULL,NULL,'2026-09-05 17:40:51'),(63,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 17:47:34'),(64,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 18:01:58'),(65,2,'LOGIN',NULL,NULL,NULL,'2026-09-05 21:24:13'),(66,2,'LOGOUT',NULL,NULL,NULL,'2026-09-05 21:24:49'),(67,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 21:24:53'),(68,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 21:47:47'),(69,1,'LOGOUT',NULL,NULL,NULL,'2026-09-05 21:47:54'),(70,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 21:48:04'),(71,1,'LOGOUT',NULL,NULL,NULL,'2026-09-05 21:48:45'),(72,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 22:07:09'),(73,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 22:14:35'),(74,1,'PAYMETHOD_ADD','payment_method',5,'gcash','2026-09-05 22:21:25'),(75,1,'PAYMETHOD_DELETE','payment_method',5,'gcash','2026-09-05 22:21:35'),(76,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 22:53:14'),(77,1,'PAYMETHOD_DELETE','payment_method',6,'GCash','2026-09-05 22:55:18'),(78,1,'PAYMETHOD_ADD','payment_method',7,'Maya','2026-09-05 22:55:36'),(79,1,'LOGOUT',NULL,NULL,NULL,'2026-09-05 22:55:47'),(80,2,'LOGIN',NULL,NULL,NULL,'2026-09-05 22:55:52'),(81,2,'LOGOUT',NULL,NULL,NULL,'2026-09-05 22:56:11'),(82,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 23:03:51'),(83,1,'PAYMETHOD_DELETE','payment_method',7,'Maya','2026-09-05 23:05:06'),(84,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 23:17:37'),(85,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 23:30:45'),(86,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 23:39:49'),(87,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 23:43:12'),(88,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 23:46:52'),(89,1,'LOGIN',NULL,NULL,NULL,'2026-09-05 23:52:09'),(90,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 16:46:26'),(91,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 16:51:31'),(92,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 17:04:35'),(93,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 17:13:10'),(94,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 17:24:34'),(95,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 17:26:38'),(96,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 17:31:49'),(97,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 17:40:09'),(98,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 17:42:24'),(99,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 17:49:37'),(100,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 17:54:17'),(101,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 18:02:24'),(102,1,'LOGIN',NULL,NULL,NULL,'2026-09-06 18:05:08'),(103,5,'LOGIN',NULL,NULL,NULL,'2026-09-08 00:06:39'),(104,5,'LOGOUT',NULL,NULL,NULL,'2026-09-08 00:06:43'),(105,2,'LOGIN',NULL,NULL,NULL,'2026-09-08 00:35:05'),(106,2,'LOGOUT',NULL,NULL,NULL,'2026-09-08 00:35:38'),(107,5,'LOGIN',NULL,NULL,NULL,'2026-09-08 19:36:05'),(108,2,'LOGIN',NULL,NULL,NULL,'2026-09-11 10:22:39'),(109,2,'LOGOUT',NULL,NULL,NULL,'2026-09-11 10:23:20'),(110,1,'LOGIN',NULL,NULL,NULL,'2026-09-11 10:23:24'),(111,1,'LOGOUT',NULL,NULL,NULL,'2026-09-11 10:23:39'),(112,3,'LOGIN',NULL,NULL,NULL,'2026-09-11 10:23:47'),(113,3,'LOGOUT',NULL,NULL,NULL,'2026-09-11 10:23:52'),(114,5,'LOGIN',NULL,NULL,NULL,'2026-09-11 10:23:57'),(115,5,'SUPPLIER_EDIT','supplier',5,'Test Name Name name','2026-09-11 10:24:17'),(116,5,'LOGOUT',NULL,NULL,NULL,'2026-09-11 10:24:19'),(117,5,'LOGIN',NULL,NULL,NULL,'2026-09-11 10:24:24'),(118,5,'LOGIN',NULL,NULL,NULL,'2026-09-11 10:26:34'),(119,5,'LOGOUT',NULL,NULL,NULL,'2026-09-11 10:26:38');
/*!40000 ALTER TABLE `activity_logs` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `cash_counts`
--

DROP TABLE IF EXISTS `cash_counts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `cash_counts` (
  `count_id` int(11) NOT NULL AUTO_INCREMENT,
  `shift_id` int(11) NOT NULL,
  `denomination` decimal(7,2) NOT NULL,
  `is_bill` tinyint(1) NOT NULL,
  `qty` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`count_id`),
  UNIQUE KEY `uq_count` (`shift_id`,`denomination`,`is_bill`),
  CONSTRAINT `fk_counts_shift` FOREIGN KEY (`shift_id`) REFERENCES `shifts` (`shift_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `cash_counts`
--

LOCK TABLES `cash_counts` WRITE;
/*!40000 ALTER TABLE `cash_counts` DISABLE KEYS */;
/*!40000 ALTER TABLE `cash_counts` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `cash_movements`
--

DROP TABLE IF EXISTS `cash_movements`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `cash_movements` (
  `movement_id` int(11) NOT NULL AUTO_INCREMENT,
  `shift_id` int(11) NOT NULL,
  `movement_type` enum('PayIn','PayOut','Refund','Drop') NOT NULL,
  `amount` decimal(10,2) NOT NULL,
  `reason` varchar(150) DEFAULT NULL,
  `created_by` int(11) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`movement_id`),
  KEY `fk_cashmove_shift` (`shift_id`),
  KEY `fk_cashmove_user` (`created_by`),
  CONSTRAINT `fk_cashmove_shift` FOREIGN KEY (`shift_id`) REFERENCES `shifts` (`shift_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_cashmove_user` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `cash_movements`
--

LOCK TABLES `cash_movements` WRITE;
/*!40000 ALTER TABLE `cash_movements` DISABLE KEYS */;
/*!40000 ALTER TABLE `cash_movements` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `categories`
--

DROP TABLE IF EXISTS `categories`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `categories` (
  `category_id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(50) NOT NULL,
  PRIMARY KEY (`category_id`),
  UNIQUE KEY `name` (`name`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `categories`
--

LOCK TABLES `categories` WRITE;
/*!40000 ALTER TABLE `categories` DISABLE KEYS */;
INSERT INTO `categories` VALUES (4,'Accessories'),(1,'Paint'),(3,'Thinner'),(2,'Tools');
/*!40000 ALTER TABLE `categories` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `color_formulas`
--

DROP TABLE IF EXISTS `color_formulas`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `color_formulas` (
  `formula_id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(100) NOT NULL,
  `paint_type` varchar(30) NOT NULL,
  `base_product_id` int(11) DEFAULT NULL,
  `size_label` varchar(20) DEFAULT NULL,
  `color_hex` char(7) DEFAULT NULL,
  `notes` varchar(255) DEFAULT NULL,
  `created_by` int(11) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`formula_id`),
  KEY `fk_formula_base` (`base_product_id`),
  KEY `fk_formula_user` (`created_by`),
  CONSTRAINT `fk_formula_base` FOREIGN KEY (`base_product_id`) REFERENCES `products` (`product_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_formula_user` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `color_formulas`
--

LOCK TABLES `color_formulas` WRITE;
/*!40000 ALTER TABLE `color_formulas` DISABLE KEYS */;
/*!40000 ALTER TABLE `color_formulas` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `formula_components`
--

DROP TABLE IF EXISTS `formula_components`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `formula_components` (
  `component_id` int(11) NOT NULL AUTO_INCREMENT,
  `formula_id` int(11) NOT NULL,
  `colorant_name` varchar(50) NOT NULL,
  `amount_ml` decimal(8,2) NOT NULL,
  PRIMARY KEY (`component_id`),
  KEY `fk_component_formula` (`formula_id`),
  CONSTRAINT `fk_component_formula` FOREIGN KEY (`formula_id`) REFERENCES `color_formulas` (`formula_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `formula_components`
--

LOCK TABLES `formula_components` WRITE;
/*!40000 ALTER TABLE `formula_components` DISABLE KEYS */;
/*!40000 ALTER TABLE `formula_components` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `goods_receipts`
--

DROP TABLE IF EXISTS `goods_receipts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `goods_receipts` (
  `receipt_id` int(11) NOT NULL AUTO_INCREMENT,
  `po_id` int(11) NOT NULL,
  `received_by` int(11) DEFAULT NULL,
  `receiver_name` varchar(120) NOT NULL,
  `condition_note` enum('Good','Discrepancy') NOT NULL DEFAULT 'Good',
  `remarks` varchar(500) DEFAULT NULL,
  `attested` tinyint(1) NOT NULL DEFAULT 1,
  `total_units` int(11) NOT NULL DEFAULT 0,
  `received_at` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`receipt_id`),
  KEY `idx_gr_po` (`po_id`),
  KEY `idx_gr_user` (`received_by`)
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `goods_receipts`
--

LOCK TABLES `goods_receipts` WRITE;
/*!40000 ALTER TABLE `goods_receipts` DISABLE KEYS */;
/*!40000 ALTER TABLE `goods_receipts` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `password_reset_requests`
--

DROP TABLE IF EXISTS `password_reset_requests`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `password_reset_requests` (
  `request_id` int(11) NOT NULL AUTO_INCREMENT,
  `user_id` int(11) DEFAULT NULL,
  `username_typed` varchar(50) NOT NULL,
  `status` enum('Pending','Handled','Dismissed') NOT NULL DEFAULT 'Pending',
  `requested_at` datetime NOT NULL DEFAULT current_timestamp(),
  `handled_by` int(11) DEFAULT NULL,
  `handled_at` datetime DEFAULT NULL,
  PRIMARY KEY (`request_id`),
  KEY `idx_prr_status` (`status`),
  KEY `idx_prr_user` (`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `password_reset_requests`
--

LOCK TABLES `password_reset_requests` WRITE;
/*!40000 ALTER TABLE `password_reset_requests` DISABLE KEYS */;
/*!40000 ALTER TABLE `password_reset_requests` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `payment_methods`
--

DROP TABLE IF EXISTS `payment_methods`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `payment_methods` (
  `method_id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(30) NOT NULL,
  `type` enum('Cash','Reference') NOT NULL DEFAULT 'Reference',
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `note` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`method_id`),
  UNIQUE KEY `name` (`name`)
) ENGINE=InnoDB AUTO_INCREMENT=8 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `payment_methods`
--

LOCK TABLES `payment_methods` WRITE;
/*!40000 ALTER TABLE `payment_methods` DISABLE KEYS */;
INSERT INTO `payment_methods` VALUES (1,'Cash','Cash',1,NULL),(2,'Card','Reference',1,NULL),(3,'E-Wallet','Reference',1,NULL);
/*!40000 ALTER TABLE `payment_methods` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `po_items`
--

DROP TABLE IF EXISTS `po_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `po_items` (
  `po_item_id` int(11) NOT NULL AUTO_INCREMENT,
  `po_id` int(11) NOT NULL,
  `product_id` int(11) NOT NULL,
  `qty` int(11) NOT NULL,
  `unit_cost` decimal(10,2) NOT NULL,
  `line_cost` decimal(12,2) NOT NULL,
  `request_id` int(11) DEFAULT NULL,
  `shelved_qty` int(11) NOT NULL DEFAULT 0 COMMENT 'Units physically shelved by inventory; incremented by the Put Away screen.',
  PRIMARY KEY (`po_item_id`),
  KEY `fk_poitems_po` (`po_id`),
  KEY `fk_poitems_product` (`product_id`),
  KEY `idx_poi_putaway` (`po_id`,`shelved_qty`),
  CONSTRAINT `fk_poitems_po` FOREIGN KEY (`po_id`) REFERENCES `purchase_orders` (`po_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_poitems_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`product_id`) ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `po_items`
--

LOCK TABLES `po_items` WRITE;
/*!40000 ALTER TABLE `po_items` DISABLE KEYS */;
INSERT INTO `po_items` VALUES (1,1,7,10,999.00,9990.00,1,0);
/*!40000 ALTER TABLE `po_items` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `products`
--

DROP TABLE IF EXISTS `products`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `products` (
  `product_id` int(11) NOT NULL AUTO_INCREMENT,
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
  `supplier_id` int(11) DEFAULT NULL,
  PRIMARY KEY (`product_id`),
  UNIQUE KEY `sku` (`sku`),
  KEY `fk_products_category` (`category_id`),
  KEY `idx_products_active` (`is_active`),
  KEY `fk_product_supplier` (`supplier_id`),
  CONSTRAINT `fk_products_category` FOREIGN KEY (`category_id`) REFERENCES `categories` (`category_id`) ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=8 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `products`
--

LOCK TABLES `products` WRITE;
/*!40000 ALTER TABLE `products` DISABLE KEYS */;
INSERT INTO `products` VALUES (1,1,'PNT-0001','Ocean Breeze','Latex','5L','#96CDE1',NULL,550.00,0.00,37,10,1,'2026-06-14 12:08:09',NULL),(2,1,'PNT-0002','Soft Sage','Latex','4L','#91B4A0',NULL,625.00,0.00,35,10,1,'2026-06-14 12:08:09',NULL),(3,1,'PNT-0003','Warm Ivory','Latex','4L','#EBE6CD',NULL,700.00,0.00,49,10,1,'2026-06-14 12:08:09',NULL),(4,1,'PNT-0004','Charcoal Gray Ni Mark','Latex','5L','#828791',NULL,600.00,0.00,21,10,1,'2026-06-14 12:08:09',NULL),(5,1,'PNT-0005','Sunny Yellow','Latex','4L','#EBDC8C',NULL,475.00,0.00,24,10,1,'2026-06-14 12:08:09',NULL),(6,1,'PNT-0006','White Primer','Latex','4L','#FFFFFF',NULL,350.00,0.00,53,15,1,'2026-06-14 12:08:09',NULL),(7,2,'TOOL-0001','TEST',NULL,'TEST',NULL,'C:\\Users\\DELL\\source\\repos\\PaintStore_POS\\ProductImages\\PROD_200a7a95b3aa.png',999.00,999.00,1,3,1,'2026-06-21 00:11:06',NULL);
/*!40000 ALTER TABLE `products` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `purchase_orders`
--

DROP TABLE IF EXISTS `purchase_orders`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `purchase_orders` (
  `po_id` int(11) NOT NULL AUTO_INCREMENT,
  `po_no` varchar(20) NOT NULL,
  `supplier_id` int(11) NOT NULL,
  `created_by` int(11) NOT NULL,
  `order_date` date NOT NULL,
  `expected_date` date DEFAULT NULL,
  `status` enum('Draft','Ordered','Partially Received','Received','Cancelled') NOT NULL DEFAULT 'Draft',
  `total_cost` decimal(12,2) NOT NULL DEFAULT 0.00,
  `received_at` datetime DEFAULT NULL,
  `paid_at` datetime DEFAULT NULL,
  PRIMARY KEY (`po_id`),
  UNIQUE KEY `po_no` (`po_no`),
  KEY `fk_po_supplier` (`supplier_id`),
  KEY `fk_po_user` (`created_by`),
  CONSTRAINT `fk_po_supplier` FOREIGN KEY (`supplier_id`) REFERENCES `suppliers` (`supplier_id`) ON UPDATE CASCADE,
  CONSTRAINT `fk_po_user` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`) ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `purchase_orders`
--

LOCK TABLES `purchase_orders` WRITE;
/*!40000 ALTER TABLE `purchase_orders` DISABLE KEYS */;
INSERT INTO `purchase_orders` VALUES (1,'PO-260624221846',5,5,'2026-06-24','2026-06-26','Received',9990.00,'2026-06-25 16:50:04',NULL);
/*!40000 ALTER TABLE `purchase_orders` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `purchase_requests`
--

DROP TABLE IF EXISTS `purchase_requests`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `purchase_requests` (
  `request_id` int(11) NOT NULL AUTO_INCREMENT,
  `product_id` int(11) NOT NULL,
  `requested_by` int(11) NOT NULL,
  `qty_requested` int(11) NOT NULL,
  `status` enum('Pending','Approved','Rejected','Quoted','Converted') NOT NULL DEFAULT 'Pending',
  `note` varchar(150) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `reviewed_by` int(11) DEFAULT NULL,
  `reviewed_at` datetime DEFAULT NULL,
  PRIMARY KEY (`request_id`),
  KEY `fk_pr_product` (`product_id`),
  KEY `fk_pr_requester` (`requested_by`)
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `purchase_requests`
--

LOCK TABLES `purchase_requests` WRITE;
/*!40000 ALTER TABLE `purchase_requests` DISABLE KEYS */;
INSERT INTO `purchase_requests` VALUES (1,7,3,5,'Converted','Auto-suggested from Low Stock Alerts. Stock: 1, Reorder level: 3','2026-06-24 00:27:47',5,'2026-06-24 22:18:46');
/*!40000 ALTER TABLE `purchase_requests` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `quotation_items`
--

DROP TABLE IF EXISTS `quotation_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `quotation_items` (
  `item_id` int(11) NOT NULL AUTO_INCREMENT,
  `quotation_id` int(11) NOT NULL,
  `product_id` int(11) NOT NULL,
  `qty_requested` int(11) NOT NULL DEFAULT 1,
  `unit_price` decimal(10,2) NOT NULL DEFAULT 0.00,
  PRIMARY KEY (`item_id`),
  KEY `fk_qitem_quotation` (`quotation_id`),
  KEY `fk_qitem_product` (`product_id`),
  CONSTRAINT `fk_qitem_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`product_id`),
  CONSTRAINT `fk_qitem_quotation` FOREIGN KEY (`quotation_id`) REFERENCES `quotations` (`quotation_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `quotation_items`
--

LOCK TABLES `quotation_items` WRITE;
/*!40000 ALTER TABLE `quotation_items` DISABLE KEYS */;
/*!40000 ALTER TABLE `quotation_items` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `quotations`
--

DROP TABLE IF EXISTS `quotations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `quotations` (
  `quotation_id` int(11) NOT NULL AUTO_INCREMENT,
  `quotation_no` varchar(20) NOT NULL,
  `request_id` int(11) NOT NULL,
  `supplier_id` int(11) NOT NULL,
  `created_by` int(11) NOT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `valid_until` date DEFAULT NULL,
  `status` enum('Draft','Sent','Accepted','Rejected','Expired') NOT NULL DEFAULT 'Draft',
  `notes` varchar(300) DEFAULT NULL,
  `total_amount` decimal(12,2) NOT NULL DEFAULT 0.00,
  PRIMARY KEY (`quotation_id`),
  UNIQUE KEY `uq_quotation_no` (`quotation_no`),
  KEY `fk_quot_request` (`request_id`),
  KEY `fk_quot_supplier` (`supplier_id`),
  KEY `fk_quot_user` (`created_by`),
  CONSTRAINT `fk_quot_request` FOREIGN KEY (`request_id`) REFERENCES `purchase_requests` (`request_id`),
  CONSTRAINT `fk_quot_supplier` FOREIGN KEY (`supplier_id`) REFERENCES `suppliers` (`supplier_id`),
  CONSTRAINT `fk_quot_user` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `quotations`
--

LOCK TABLES `quotations` WRITE;
/*!40000 ALTER TABLE `quotations` DISABLE KEYS */;
/*!40000 ALTER TABLE `quotations` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `return_items`
--

DROP TABLE IF EXISTS `return_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `return_items` (
  `return_item_id` int(11) NOT NULL AUTO_INCREMENT,
  `return_id` int(11) NOT NULL,
  `sale_item_id` int(11) NOT NULL,
  `qty_returned` int(11) NOT NULL,
  `refund_amount` decimal(10,2) NOT NULL,
  PRIMARY KEY (`return_item_id`),
  KEY `fk_retitems_return` (`return_id`),
  KEY `fk_retitems_saleitem` (`sale_item_id`),
  CONSTRAINT `fk_retitems_return` FOREIGN KEY (`return_id`) REFERENCES `returns` (`return_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_retitems_saleitem` FOREIGN KEY (`sale_item_id`) REFERENCES `sale_items` (`sale_item_id`) ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=8 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `return_items`
--

LOCK TABLES `return_items` WRITE;
/*!40000 ALTER TABLE `return_items` DISABLE KEYS */;
/*!40000 ALTER TABLE `return_items` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `returns`
--

DROP TABLE IF EXISTS `returns`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `returns` (
  `return_id` int(11) NOT NULL AUTO_INCREMENT,
  `return_no` varchar(20) NOT NULL,
  `sale_id` int(11) NOT NULL,
  `processed_by` int(11) NOT NULL,
  `return_datetime` datetime NOT NULL DEFAULT current_timestamp(),
  `reason` varchar(100) NOT NULL,
  `refund_method` enum('Cash','CardReversal','EWallet','StoreCredit') NOT NULL,
  `restocking_fee_pct` decimal(5,2) NOT NULL DEFAULT 0.00,
  `total_refund` decimal(10,2) NOT NULL DEFAULT 0.00,
  PRIMARY KEY (`return_id`),
  UNIQUE KEY `return_no` (`return_no`),
  KEY `fk_returns_sale` (`sale_id`),
  KEY `fk_returns_user` (`processed_by`),
  CONSTRAINT `fk_returns_sale` FOREIGN KEY (`sale_id`) REFERENCES `sales` (`sale_id`) ON UPDATE CASCADE,
  CONSTRAINT `fk_returns_user` FOREIGN KEY (`processed_by`) REFERENCES `users` (`user_id`) ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `returns`
--

LOCK TABLES `returns` WRITE;
/*!40000 ALTER TABLE `returns` DISABLE KEYS */;
/*!40000 ALTER TABLE `returns` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `roles`
--

DROP TABLE IF EXISTS `roles`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `roles` (
  `role_id` int(11) NOT NULL AUTO_INCREMENT,
  `role_name` varchar(30) NOT NULL,
  `description` varchar(150) DEFAULT NULL,
  PRIMARY KEY (`role_id`),
  UNIQUE KEY `role_name` (`role_name`)
) ENGINE=InnoDB AUTO_INCREMENT=6 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `roles`
--

LOCK TABLES `roles` WRITE;
/*!40000 ALTER TABLE `roles` DISABLE KEYS */;
INSERT INTO `roles` VALUES (1,'Admin','Full access to all modules and settings'),(2,'Cashier','Sales, returns, and cash drawer'),(3,'Inventory','Stock levels, products, and adjustments'),(4,'Accountant','Reports, sales, and financial figures'),(5,'Procurement','Suppliers and purchase orders');
/*!40000 ALTER TABLE `roles` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sale_items`
--

DROP TABLE IF EXISTS `sale_items`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `sale_items` (
  `sale_item_id` int(11) NOT NULL AUTO_INCREMENT,
  `sale_id` int(11) NOT NULL,
  `product_id` int(11) DEFAULT NULL,
  `formula_id` int(11) DEFAULT NULL,
  `description` varchar(120) DEFAULT NULL,
  `qty` int(11) NOT NULL,
  `unit_price` decimal(10,2) NOT NULL,
  `unit_cost` decimal(10,2) NOT NULL DEFAULT 0.00,
  `line_total` decimal(10,2) NOT NULL,
  PRIMARY KEY (`sale_item_id`),
  KEY `fk_items_product` (`product_id`),
  KEY `fk_items_formula` (`formula_id`),
  KEY `idx_saleitems_sale` (`sale_id`),
  CONSTRAINT `fk_items_formula` FOREIGN KEY (`formula_id`) REFERENCES `color_formulas` (`formula_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_items_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`product_id`) ON UPDATE CASCADE,
  CONSTRAINT `fk_items_sale` FOREIGN KEY (`sale_id`) REFERENCES `sales` (`sale_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=48 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sale_items`
--

LOCK TABLES `sale_items` WRITE;
/*!40000 ALTER TABLE `sale_items` DISABLE KEYS */;
INSERT INTO `sale_items` VALUES (47,32,7,NULL,'TEST',7,999.00,999.00,6993.00);
/*!40000 ALTER TABLE `sale_items` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sales`
--

DROP TABLE IF EXISTS `sales`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `sales` (
  `sale_id` int(11) NOT NULL AUTO_INCREMENT,
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
  PRIMARY KEY (`sale_id`),
  UNIQUE KEY `sale_no` (`sale_no`),
  KEY `fk_sales_user` (`user_id`),
  KEY `fk_sales_shift` (`shift_id`),
  KEY `fk_sales_method` (`method_id`),
  KEY `idx_sales_datetime` (`sale_datetime`),
  KEY `idx_sales_status` (`status`),
  KEY `idx_sales_reference_no` (`reference_no`),
  CONSTRAINT `fk_sales_method` FOREIGN KEY (`method_id`) REFERENCES `payment_methods` (`method_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_sales_shift` FOREIGN KEY (`shift_id`) REFERENCES `shifts` (`shift_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `fk_sales_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=33 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sales`
--

LOCK TABLES `sales` WRITE;
/*!40000 ALTER TABLE `sales` DISABLE KEYS */;
INSERT INTO `sales` VALUES (32,'TXN-20260623194843',2,NULL,1,'2026-06-23 19:48:43','Completed','None',6993.00,0.00,0.00,6993.00,7000.00,7.00,NULL);
/*!40000 ALTER TABLE `sales` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `shifts`
--

DROP TABLE IF EXISTS `shifts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `shifts` (
  `shift_id` int(11) NOT NULL AUTO_INCREMENT,
  `user_id` int(11) NOT NULL,
  `register_no` varchar(10) NOT NULL DEFAULT '01',
  `opening_float` decimal(10,2) NOT NULL DEFAULT 0.00,
  `started_at` datetime NOT NULL DEFAULT current_timestamp(),
  `ended_at` datetime DEFAULT NULL,
  `expected_cash` decimal(10,2) DEFAULT NULL,
  `counted_cash` decimal(10,2) DEFAULT NULL,
  `variance` decimal(10,2) DEFAULT NULL,
  `status` enum('Open','Closed') NOT NULL DEFAULT 'Open',
  PRIMARY KEY (`shift_id`),
  KEY `fk_shifts_user` (`user_id`),
  CONSTRAINT `fk_shifts_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `shifts`
--

LOCK TABLES `shifts` WRITE;
/*!40000 ALTER TABLE `shifts` DISABLE KEYS */;
/*!40000 ALTER TABLE `shifts` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `stock_movements`
--

DROP TABLE IF EXISTS `stock_movements`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `stock_movements` (
  `movement_id` int(11) NOT NULL AUTO_INCREMENT,
  `product_id` int(11) NOT NULL,
  `user_id` int(11) DEFAULT NULL,
  `movement_type` enum('Initial','Sale','Return','Purchase','Adjustment') NOT NULL,
  `qty_change` int(11) NOT NULL,
  `reference_type` varchar(20) DEFAULT NULL,
  `reference_id` int(11) DEFAULT NULL,
  `note` varchar(150) DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`movement_id`),
  KEY `fk_stock_user` (`user_id`),
  KEY `idx_stock_product` (`product_id`),
  CONSTRAINT `fk_stock_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`product_id`) ON UPDATE CASCADE,
  CONSTRAINT `fk_stock_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=44 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `stock_movements`
--

LOCK TABLES `stock_movements` WRITE;
/*!40000 ALTER TABLE `stock_movements` DISABLE KEYS */;
INSERT INTO `stock_movements` VALUES (41,7,3,'Purchase',5,'receive',NULL,'New Arrival','2026-06-22 00:34:13'),(42,7,2,'Sale',-7,NULL,NULL,NULL,'2026-06-23 19:48:43'),(43,7,5,'Purchase',10,'receive',1,'Received via PO PO-260624221846','2026-06-25 16:50:04');
/*!40000 ALTER TABLE `stock_movements` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `store_settings`
--

DROP TABLE IF EXISTS `store_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
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
  PRIMARY KEY (`setting_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `store_settings`
--

LOCK TABLES `store_settings` WRITE;
/*!40000 ALTER TABLE `store_settings` DISABLE KEYS */;
INSERT INTO `store_settings` VALUES (1,'PAINT STORE POS','Sales Receipt',NULL,NULL,NULL,12.00,1,'2026-08-05 10:10:45');
/*!40000 ALTER TABLE `store_settings` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `suppliers`
--

DROP TABLE IF EXISTS `suppliers`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `suppliers` (
  `supplier_id` int(11) NOT NULL AUTO_INCREMENT,
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
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`supplier_id`)
) ENGINE=InnoDB AUTO_INCREMENT=8 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `suppliers`
--

LOCK TABLES `suppliers` WRITE;
/*!40000 ALTER TABLE `suppliers` DISABLE KEYS */;
INSERT INTO `suppliers` VALUES (5,'Test Name Name name',NULL,'Test Name Name name',NULL,'095106946647','Test@gmail.com',NULL,NULL,NULL,'Macanaya, Aparri',NULL,NULL,'SupplierImages\\SUP_e288fb1affad.png',1),(6,'Valorant',NULL,'Juan Dela Cruz',NULL,'12345','Valorant@gmail.com',NULL,NULL,NULL,'Punta Cagayan',NULL,NULL,NULL,1);
/*!40000 ALTER TABLE `suppliers` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `users`
--

DROP TABLE IF EXISTS `users`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `users` (
  `user_id` int(11) NOT NULL AUTO_INCREMENT,
  `role_id` int(11) NOT NULL,
  `full_name` varchar(100) NOT NULL,
  `username` varchar(50) NOT NULL,
  `password_hash` varchar(255) NOT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `status` enum('Pending','Approved','Rejected') NOT NULL DEFAULT 'Approved',
  `requested_role_id` int(11) DEFAULT NULL,
  `reviewed_by` int(11) DEFAULT NULL,
  `reviewed_at` datetime DEFAULT NULL,
  `reject_reason` varchar(255) DEFAULT NULL,
  `must_change_password` tinyint(1) NOT NULL DEFAULT 0,
  `created_at` datetime NOT NULL DEFAULT current_timestamp(),
  `sales_quota` decimal(10,2) NOT NULL DEFAULT 0.00,
  PRIMARY KEY (`user_id`),
  UNIQUE KEY `username` (`username`),
  KEY `fk_users_role` (`role_id`),
  CONSTRAINT `fk_users_role` FOREIGN KEY (`role_id`) REFERENCES `roles` (`role_id`) ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=8 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `users`
--

LOCK TABLES `users` WRITE;
/*!40000 ALTER TABLE `users` DISABLE KEYS */;
INSERT INTO `users` VALUES (1,1,'System Administrator','admin','100000:KkImNeukCua9+lFhfFYdLQ==:1Fi3J9rz3+ZyIlIXLsXeBuFbvzOcyT9hWiAHRwvIE3o=',1,'Approved',NULL,NULL,NULL,NULL,0,'2026-06-14 12:08:09',0.00),(2,2,'Test Cashier','cashier','100000:MplcoBlF+pI15psscpbSHQ==:4uYH1lmsHLyuILwYXpOXtkVmLgBeqKoit41MGz4oAnc=',1,'Approved',NULL,NULL,NULL,NULL,0,'2026-06-14 12:08:09',0.00),(3,3,'Test Inventory','inventory','100000:A1d7XpQrWiIh63F8i6bL0A==:hcEp/yP6gV4Lyovg0YDMgM1zEED2T0Dv/k7TsxQ1gCI=',1,'Approved',NULL,NULL,NULL,NULL,0,'2026-06-14 12:08:09',0.00),(4,4,'Test Accountant','accountant','100000:yvm3MYbrzXrYctmCnxR4Kw==:diIZIDrbZMnYoO8/WjuaSrFvRy1OBkX/d4VM1neGwcU=',1,'Approved',NULL,NULL,NULL,NULL,0,'2026-06-14 12:08:09',0.00),(5,5,'Test Procurement','procurement','100000:ieS/j7nLWOJoO42LH8aITw==:bLjrEm755GnRSxjWp7nAKQExHb338VSN8gMrOQB4bl0=',1,'Approved',NULL,NULL,NULL,NULL,0,'2026-06-14 12:08:09',0.00),(6,2,'Chang','Chanzelyn','100000:hIwAoRKHiLGEo481ek7H3g==:dfohprxUqXJtHIlRhj+uJF8WaQxBhRQjKrS5+pTbYnk=',1,'Approved',NULL,NULL,NULL,NULL,0,'2026-08-04 23:09:35',5000.00);
/*!40000 ALTER TABLE `users` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Dumping events for database 'paint_store_pos'
--

--
-- Dumping routines for database 'paint_store_pos'
--
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2026-09-11 10:26:41
