Create DATABASE if not exists IMP;
USE IMP;

CREATE TABLE if not exists EMPLOYEE_ACCOUNT (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    pin_hash VARCHAR(255) NOT NULL,
    is_activated BOOLEAN NOT NULL DEFAULT TRUE,
    last_sign_in_at DATETIME NULL
);

CREATE TABLE if not exists MASTER_ACCOUNT(
    id INT PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    email VARCHAR(255) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    two_factor_secret VARCHAR(255) NULL,
    alert_threshold_days SMALLINT NOT NULL DEFAULT 180 CHECK (alert_threshold_days >= 1)
);

CREATE TABLE if not exists DEVICE(
    id INT PRIMARY KEY AUTO_INCREMENT,
    device_name VARCHAR(100) NOT NULL UNIQUE,
    active_employee_id INT NULL,
    is_elevated BOOLEAN NOT NULL DEFAULT FALSE,
    last_activity_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    FOREIGN KEY (active_employee_id) REFERENCES EMPLOYEE_ACCOUNT(id)
);

CREATE TABLE if not exists BUILDING(
    id INT PRIMARY KEY AUTO_INCREMENT,
    name VARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE if not exists CATEGORY(
    id INT PRIMARY KEY AUTO_INCREMENT,
    name VARCHAR(100) NOT NULL UNIQUE,
    symbol VARCHAR(16) NULL
);

CREATE TABLE if not exists BOX(
    id INT PRIMARY KEY AUTO_INCREMENT,
    category_id INT NOT NULL,
    building_id INT NOT NULL,
    shelf_letter VARCHAR(3) NOT NULL CHECK (REGEXP_LIKE(shelf_letter,'^[A-Z]{1,3}$', 'c')),
    shelf_level SMALLINT NOT NULL CHECK (shelf_level BETWEEN 1 AND 3),
    fullness ENUM ('EMPTY', 'HALF', 'FULL') NOT NULL,
    notes TEXT NULL,
    is_racked BOOLEAN NOT NULL DEFAULT TRUE,
    racked_since DATE NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by INT NOT NULL,
    last_updated_at DATETIME NULL,
    last_updated_by INT NULL,
    deleted_at DATETIME NULL,
    deleted_by INT NULL,

    FOREIGN KEY (category_id) REFERENCES CATEGORY(id),
    FOREIGN KEY (building_id) REFERENCES BUILDING(id),
    FOREIGN KEY (created_by) REFERENCES EMPLOYEE_ACCOUNT(id),
    FOREIGN KEY (last_updated_by) REFERENCES EMPLOYEE_ACCOUNT(id),
    FOREIGN KEY (deleted_by) REFERENCES EMPLOYEE_ACCOUNT(id)
);

Create TABLE if not exists AUDIT_LOG(
    id INT PRIMARY KEY AUTO_INCREMENT,
    timestamp DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    employee_id INT NOT NULL,
    is_elevated BOOLEAN NOT NULL DEFAULT FALSE,
    device_id INT NOT NULL,
    action_type ENUM ('ADDED', 'EDITED', 'DELETED', 'RESTORED', 'PERMANENTLY_DELETED', 'RACKED', 'UNRACKED', 'ACTIVATED', 'DEACTIVATED') NOT NULL,
    target_type ENUM('BOX', 'BUILDING', 'CATEGORY', 'EMPLOYEE', 'MASTER') NOT NULL,
    target_id INT NOT NULL,
    target_label VARCHAR(100) NOT NULL,
    details JSON NULL,

    FOREIGN KEY (employee_id) REFERENCES EMPLOYEE_ACCOUNT(id),
    FOREIGN KEY (device_id) REFERENCES DEVICE(id)
);


