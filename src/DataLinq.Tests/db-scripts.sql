ALTER TABLE `dept_emp`
	DROP FOREIGN KEY `dept_emp_ibfk_1`;

ALTER TABLE `dept_manager`
	DROP FOREIGN KEY `dept_manager_ibfk_1`;

ALTER TABLE `salaries`
	DROP FOREIGN KEY `salaries_ibfk_1`;

ALTER TABLE `titles`
	DROP FOREIGN KEY `titles_ibfk_1`;


ALTER TABLE `employees`
	CHANGE COLUMN `emp_no` `emp_no` INT(11) NOT NULL AUTO_INCREMENT FIRST;


ALTER TABLE `dept_emp`
    ADD CONSTRAINT `dept_emp_ibfk_1` FOREIGN KEY (`emp_no`) REFERENCES `employees`.`employees` (`emp_no`) ON UPDATE RESTRICT ON DELETE CASCADE;

ALTER TABLE `dept_manager`
    ADD CONSTRAINT `dept_manager_ibfk_1` FOREIGN KEY (`emp_no`) REFERENCES `employees`.`employees` (`emp_no`) ON UPDATE RESTRICT ON DELETE CASCADE;

ALTER TABLE `salaries`
    ADD CONSTRAINT `salaries_ibfk_1` FOREIGN KEY (`emp_no`) REFERENCES `employees`.`employees` (`emp_no`) ON UPDATE RESTRICT ON DELETE CASCADE;

ALTER TABLE `titles`
    ADD CONSTRAINT `titles_ibfk_1` FOREIGN KEY (`emp_no`) REFERENCES `employees`.`employees` (`emp_no`) ON UPDATE RESTRICT ON DELETE CASCADE;