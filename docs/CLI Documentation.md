# DataLinq CLI Documentation

The DataLinq CLI tool lets you manage your database and model generation tasks from the command line. It is installed as a global dotnet tool. Below is a summary of the available commands and their options.

---

## General Options

All commands accept the following general options:

- **-v, --verbose**  
  Enable verbose output for more detailed logging.

- **-c, --config**  
  Specify the path to the configuration file (e.g., `datalinq.json`).  
  *(Optional)*

---

## Commands

### 1. create-database

**Purpose:**  
Creates the target database using the model metadata and configuration settings.

**Usage:**  
```bash
datalinq create-database [options]
```

**Options:**

- **-d, --datasource**  
  *Description:* Name of the database instance on the server or the file on disk (depending on the connection type).  
  *Optional*

- **-n, --name**  
  *Description:* The name as defined in the DataLinq configuration file.  
  *Optional*

- **-t, --type**  
  *Description:* Specifies the database connection type to create the database for (e.g., MySQL, SQLite).  
  *Optional*

---

### 2. create-sql

**Purpose:**  
Generates SQL scripts for creating the database schema based on the model definitions.

**Usage:**  
```bash
datalinq create-sql -o <output-file> [other options]
```

**Options:**

- **-o, --output**  
  *Description:* Path to the output file where the generated SQL script will be saved.  
  *Required*

- **-d, --datasource**  
  *Description:* Name of the database instance on the server or the file on disk.  
  *Optional*

- **-n, --name**  
  *Description:* The name as defined in the DataLinq configuration file.  
  *Optional*

- **-t, --type**  
  *Description:* Specifies the database connection type (e.g., MySQL, SQLite).  
  *Optional*

- Additionally, the general options (`-v, --verbose` and `-c, --config`) can also be used.

---

### 3. create-models

**Purpose:**  
Generates data model classes (both immutable and mutable) directly from your database schema.

**Usage:**  
```bash
datalinq create-models [options]
```

**Options:**

- **-s, --skip-source**  
  *Description:* Skip reading from source models during generation (boolean flag).  
  *Optional*

- **-d, --datasource**  
  *Description:* Name of the database instance on the server or the file on disk.  
  *Optional*

- **-n, --name**  
  *Description:* The name as defined in the DataLinq configuration file.  
  *Optional*

- **-t, --type**  
  *Description:* Specifies the database connection type (e.g., MySQL, SQLite).  
  *Optional*

- General options (`-v, --verbose` and `-c, --config`) are also available.

---

### 4. list

**Purpose:**  
Lists all databases defined in your DataLinq configuration file.

**Usage:**  
```bash
datalinq list [options]
```

**Options:**

- **-v, --verbose**  
  *Description:* Enable verbose output for detailed listing.  
  *Optional*

- **-c, --config**  
  *Description:* Path to the configuration file (e.g., `datalinq.json`).  
  *Optional*

---

## Example Usages

- **Creating a Database:**
  ```bash
  datalinq create-database -n MyDatabase -t MySQL
  ```

- **Generating SQL Script:**
  ```bash
  datalinq create-sql -o schema.sql -n MyDatabase -t SQLite
  ```

- **Generating Models:**
  ```bash
  datalinq create-models -n MyDatabase --skip-source
  ```

- **Listing Databases from Config:**
  ```bash
  datalinq list -c ./datalinq.json -v
  ```