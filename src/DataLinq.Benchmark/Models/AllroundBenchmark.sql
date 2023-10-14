/* Generated 2023-10-14 15:30:21 by DataLinq */

CREATE TABLE IF NOT EXISTS `products` (
  `ProductId`      BINARY(16) NOT NULL,
  `CategoryId`     INT NULL,
  `ManufacturerId` INT NULL,
  `Price`          DECIMAL NULL,
  `ProductName`    VARCHAR(255) NULL,
  PRIMARY KEY (`ProductId`)
);

CREATE TABLE IF NOT EXISTS `locations` (
  `LocationId` BINARY(16) NOT NULL,
  `Address`    VARCHAR(500) NULL,
  `City`       VARCHAR(255) NULL,
  `Country`    VARCHAR(255) NULL,
  PRIMARY KEY (`LocationId`)
);

CREATE TABLE IF NOT EXISTS `inventory` (
  `InventoryId` INT NOT NULL AUTO_INCREMENT,
  `LocationId`  BINARY(16) NULL,
  `ProductId`   BINARY(16) NULL,
  `Stock`       INT NULL,
  PRIMARY KEY (`InventoryId`),
  CONSTRAINT `inventory_ibfk_2` FOREIGN KEY (`LocationId`) REFERENCES `locations` (`LocationId`) ON UPDATE RESTRICT ON DELETE RESTRICT,
  CONSTRAINT `inventory_ibfk_1` FOREIGN KEY (`ProductId`) REFERENCES `products` (`ProductId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `locationshistory` (
  `HistoryId`  BINARY(16) NOT NULL,
  `LocationId` BINARY(16) NULL,
  `ChangeDate` DATE NULL,
  `ChangeLog`  TEXT NULL,
  PRIMARY KEY (`HistoryId`),
  CONSTRAINT `locationshistory_ibfk_1` FOREIGN KEY (`LocationId`) REFERENCES `locations` (`LocationId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `manufacturers` (
  `ManufacturerId`   INT NOT NULL AUTO_INCREMENT,
  `ManufacturerName` VARCHAR(255) NULL,
  PRIMARY KEY (`ManufacturerId`)
);

CREATE TABLE IF NOT EXISTS `users` (
  `UserId`     BINARY(16) NOT NULL,
  `DateJoined` DATE NULL,
  `Email`      VARCHAR(255) NULL,
  `UserName`   VARCHAR(255) NULL,
  `UserRole`   ENUM('Admin','User','Guest') NULL,
  PRIMARY KEY (`UserId`)
);

CREATE TABLE IF NOT EXISTS `orders` (
  `OrderId`           BINARY(16) NOT NULL,
  `ProductId`         BINARY(16) NULL,
  `UserId`            BINARY(16) NULL,
  `OrderDate`         DATE NULL,
  `OrderStatus`       ENUM('Placed','Shipped','Delivered','Cancelled') NULL,
  `ShippingCompanyId` INT NULL,
  PRIMARY KEY (`OrderId`),
  CONSTRAINT `orders_ibfk_2` FOREIGN KEY (`ProductId`) REFERENCES `products` (`ProductId`) ON UPDATE RESTRICT ON DELETE RESTRICT,
  CONSTRAINT `orders_ibfk_1` FOREIGN KEY (`UserId`) REFERENCES `users` (`UserId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `payments` (
  `PaymentId`     INT NOT NULL AUTO_INCREMENT,
  `OrderId`       BINARY(16) NULL,
  `Amount`        DECIMAL NULL,
  `PaymentDate`   DATE NULL,
  `PaymentMethod` ENUM('CreditCard','DebitCard','PayPal','BankTransfer') NULL,
  PRIMARY KEY (`PaymentId`),
  CONSTRAINT `payments_ibfk_1` FOREIGN KEY (`OrderId`) REFERENCES `orders` (`OrderId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `productcategories` (
  `CategoryId`   BINARY(16) NOT NULL,
  `CategoryName` VARCHAR(255) NULL,
  PRIMARY KEY (`CategoryId`)
);

CREATE TABLE IF NOT EXISTS `productimages` (
  `ImageId`   BINARY(16) NOT NULL,
  `ProductId` BINARY(16) NULL,
  `ImageURL`  TEXT NULL,
  PRIMARY KEY (`ImageId`),
  CONSTRAINT `productimages_ibfk_1` FOREIGN KEY (`ProductId`) REFERENCES `products` (`ProductId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `productreviews` (
  `ReviewId`  BINARY(16) NOT NULL,
  `ProductId` BINARY(16) NULL,
  `UserId`    BINARY(16) NULL,
  `Rating`    TINYINT NULL,
  `Review`    TEXT NULL,
  PRIMARY KEY (`ReviewId`),
  CONSTRAINT `productreviews_ibfk_2` FOREIGN KEY (`ProductId`) REFERENCES `products` (`ProductId`) ON UPDATE RESTRICT ON DELETE RESTRICT,
  CONSTRAINT `productreviews_ibfk_1` FOREIGN KEY (`UserId`) REFERENCES `users` (`UserId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `discounts` (
  `DiscountId`         INT NOT NULL AUTO_INCREMENT,
  `ProductId`          BINARY(16) NULL,
  `DiscountPercentage` DECIMAL NULL,
  `EndDate`            DATE NULL,
  `StartDate`          DATE NULL,
  PRIMARY KEY (`DiscountId`),
  CONSTRAINT `discounts_ibfk_1` FOREIGN KEY (`ProductId`) REFERENCES `products` (`ProductId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `producttags` (
  `TagId`      INT NOT NULL AUTO_INCREMENT,
  `CategoryId` BINARY(16) NULL,
  `TagName`    VARCHAR(255) NULL,
  PRIMARY KEY (`TagId`),
  CONSTRAINT `producttags_ibfk_1` FOREIGN KEY (`CategoryId`) REFERENCES `productcategories` (`CategoryId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `shippingcompanies` (
  `ShippingCompanyId` INT NOT NULL AUTO_INCREMENT,
  `CompanyName`       VARCHAR(255) NULL,
  PRIMARY KEY (`ShippingCompanyId`)
);

CREATE TABLE IF NOT EXISTS `userprofiles` (
  `ProfileId` BINARY(16) NOT NULL,
  `UserId`    BINARY(16) NULL,
  `Bio`       TEXT NULL,
  PRIMARY KEY (`ProfileId`),
  CONSTRAINT `userprofiles_ibfk_1` FOREIGN KEY (`UserId`) REFERENCES `users` (`UserId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `userfeedback` (
  `FeedbackId` INT NOT NULL AUTO_INCREMENT,
  `ProductId`  BINARY(16) NULL,
  `UserId`     BINARY(16) NULL,
  `Feedback`   TEXT NULL,
  PRIMARY KEY (`FeedbackId`),
  CONSTRAINT `userfeedback_ibfk_2` FOREIGN KEY (`ProductId`) REFERENCES `products` (`ProductId`) ON UPDATE RESTRICT ON DELETE RESTRICT,
  CONSTRAINT `userfeedback_ibfk_1` FOREIGN KEY (`UserId`) REFERENCES `users` (`UserId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `userhistory` (
  `HistoryId`    INT NOT NULL AUTO_INCREMENT,
  `UserId`       BINARY(16) NULL,
  `ActivityDate` DATE NULL,
  `ActivityLog`  TEXT NULL,
  PRIMARY KEY (`HistoryId`),
  CONSTRAINT `userhistory_ibfk_1` FOREIGN KEY (`UserId`) REFERENCES `users` (`UserId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `usercontacts` (
  `ContactId` INT NOT NULL AUTO_INCREMENT,
  `ProfileId` BINARY(16) NULL,
  `Phone`     VARCHAR(20) NULL,
  PRIMARY KEY (`ContactId`),
  CONSTRAINT `usercontacts_ibfk_1` FOREIGN KEY (`ProfileId`) REFERENCES `userprofiles` (`ProfileId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS `orderdetails` (
  `DetailId`  BINARY(16) NOT NULL,
  `OrderId`   BINARY(16) NULL,
  `ProductId` BINARY(16) NULL,
  `Quantity`  INT NULL,
  PRIMARY KEY (`DetailId`),
  CONSTRAINT `orderdetails_ibfk_1` FOREIGN KEY (`OrderId`) REFERENCES `orders` (`OrderId`) ON UPDATE RESTRICT ON DELETE RESTRICT,
  CONSTRAINT `orderdetails_ibfk_2` FOREIGN KEY (`ProductId`) REFERENCES `products` (`ProductId`) ON UPDATE RESTRICT ON DELETE RESTRICT
);

