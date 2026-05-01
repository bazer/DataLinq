# DataLinq CLI Documentation

The DataLinq CLI tool lets you inspect configuration, generate models, generate SQL, create databases, and validate model metadata against a live database from the command line. It is installed as the global `datalinq` tool.

## Overview

```mermaid
---
config:
  theme: neo
  look: classic
---
graph TD
    subgraph "DataLinq CLI Tool"

            ConfigFile["datalinq.json<br/><b>(Required Config)</b>"]:::FileStyle
            UserConfigFile["datalinq.user.json<br/><i>(Optional Overrides)</i>"]:::FileStyle
        CLI(datalinq):::ToolStyle -- Global Options --> GlobalOpts["-v, --verbose<br/>-c, --config path-or-directory"]

        ConfigFile -- Reads --> CLI
        UserConfigFile -.->|Overrides| ConfigFile

        CLI -- command --> CreateDB["create-database<br/><i>Create target database</i>"]
        CLI -- command --> CreateSQL["create-sql<br/><i>Generate schema SQL script</i>"]
        CLI -- command --> CreateModels["create-models<br/><i>Generate models from DB</i>"]
        CLI -- command --> Validate["validate<br/><i>Report schema drift</i>"]
        CLI -- command --> ListCmd["list<br/><i>List configured databases</i>"]

        CreateDB -- Options --> CD_Opts["-d, --datasource name<br/>-n, --name config_name<br/>-t, --type db_type"]
        CreateSQL -- Options --> CS_Opts["<b>-o, --output path</b> (Required)<br/>-d, --datasource name<br/>-n, --name config_name<br/>-t, --type db_type"]
        CreateModels -- Options --> CM_Opts["--skip-source<br/>--overwrite-types<br/>-d, --datasource name<br/>-n, --name config_name<br/>-t, --type db_type"]
        Validate -- Options --> Val_Opts["--output text|json<br/>-d, --datasource name<br/>-n, --name config_name<br/>-t, --type db_type"]
        ListCmd -- Options --> List_Opts["(Uses global options)"]

        CLI:::ToolStyle
        CreateDB:::CommandStyle
        CreateSQL:::CommandStyle
        CreateModels:::CommandStyle
        Validate:::CommandStyle
        ListCmd:::CommandStyle
        CD_Opts:::OptionsStyle
        CS_Opts:::OptionsStyle
        CM_Opts:::OptionsStyle
        Val_Opts:::OptionsStyle
        List_Opts:::OptionsStyle
        GlobalOpts:::OptionsStyle
        ConfigFile:::FileStyle
        UserConfigFile:::FileStyle

    end

    classDef ToolStyle stroke-width:2px, stroke:#FFB74D, fill:#FFF8E1, color:#E65100
    classDef CommandStyle stroke-width:1px, stroke:#374D7C, fill:#E2EBFF, color:#374D7C
    classDef OptionsStyle stroke-width:1px, stroke:#AAAAAA, fill:#FAFAFA, color:#333333, font-family:monospace, font-size:0.9em
    classDef FileStyle stroke-width:1px, stroke:#81C784, fill:#E8F5E9, color:#1B5E20
    linkStyle default stroke:#000000
    linkStyle 1 stroke-dasharray: 5 5
```

---

## General Options

All commands accept the following general options:

- **-v, --verbose**  
  Enable verbose output for more detailed logging.

- **-c, --config**  
  Specify either the path to `datalinq.json` or a directory containing it.  
  *(Optional)*

## Selection Rules

The CLI resolves a target database from the config before it can do any work.

- If the config contains more than one database, you must pass `-n` or `--name`.
- If the selected database contains more than one connection type, you must pass `-t` or `--type`.
- `datalinq.user.json` is discovered by replacing `.json` with `.user.json` next to the main config file, then merged on top.

In the CLI entry point, the built-in registered providers are:

- `MySQL`
- `MariaDB`
- `SQLite`

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
Generates data model classes directly from your database schema.

**Usage:**  
```bash
datalinq create-models [options]
```

**Options:**

- **--skip-source**  
  *Description:* Skip reading from source models during generation.
  *Optional*

- **--overwrite-types**  
  *Description:* Force C# property types in your model files to be overwritten with the types inferred from the database. By default, existing C# types (especially enums) are preserved.
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

**Important:**  
`create-models` is not a dry-run command. It writes generated files to the configured destination directory and is intended to refresh generated model output.

---

### 4. validate

**Purpose:**  
Loads the configured C# model metadata, reads live database metadata through the selected provider, and reports schema drift without applying changes.

**Usage:**  
```bash
datalinq validate [options]
```

**Options:**

- **--output**  
  *Description:* Output format. Supported values are `text` and `json`.  
  *Default:* `text`

- **-d, --datasource**  
  *Description:* Name of the database instance on the server or the file on disk.  
  *Optional*

- **-n, --name**  
  *Description:* The name as defined in the DataLinq configuration file.  
  *Optional*

- **-t, --type**  
  *Description:* Specifies the database connection type (e.g., MySQL, MariaDB, SQLite).  
  *Optional*

- General options (`-v, --verbose` and `-c, --config`) are also available.

**Exit codes:**

- `0`: validation completed and no schema drift was detected
- `1`: validation completed and schema drift was detected
- `2`: command, configuration, connection, model parsing, or metadata loading failed

**Important:**  
`validate` is read-only. It reports drift; it does not generate migration scripts or apply schema changes.

---

### 5. list

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
  datalinq create-models -n MyDatabase
  ```

- **Generating Models and Forcing Type Overwrite:**
  ```bash
  datalinq create-models -n MyDatabase --overwrite-types
  ```

- **Validating Models Against a Live Database:**
  ```bash
  datalinq validate -n MyDatabase -t SQLite
  ```

- **Validating with JSON Output:**
  ```bash
  datalinq validate -n MyDatabase --output json
  ```

- **Listing Databases from Config:**
  ```bash
  datalinq list -c ./datalinq.json -v
  ```

- **Using a directory instead of a file for config discovery:**
  ```bash
  datalinq create-models -c . -n MyDatabase
  ```

## Common Failure Cases

- **More than one database in config and no `-n`:**  
  The command cannot choose a database automatically and fails until you specify `--name`.

- **More than one connection type on the selected database and no `-t`:**  
  The command cannot choose a provider automatically and fails until you specify `--type`.

- **Using `datalinq.user.json` for overrides:**  
  The CLI does not scan for arbitrary override files. It only looks for the file created by replacing `.json` with `.user.json` next to the main config file.

- **Running `create-models`:**  
  This command writes generated output directly to the configured destination directory. Treat it as a refresh of generated code, not as a preview command.

- **Running `validate`:**  
  This command reads live database metadata and can return exit code `1` for real schema drift even when the command itself succeeds. Treat exit code `1` as a validation result, not a CLI crash.
