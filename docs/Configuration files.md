# DataLinq Configuration Files

DataLinq uses JSON-based configuration files to define your databases, connections, and model-generation settings. There are two configuration files:
 
- **datalinq.json**: The primary configuration file.
- **datalinq.user.json**: An optional file used to override or extend settings from datalinq.json for user-specific or local changes.

These files are used by the DataLinq CLI tool, it reads the main configuration file and then checks if a corresponding datalinq.user.json exists (by replacing the extension); if found, its settings are merged with the main configuration file.

---

## Overall Structure

Both configuration files adhere to the same schema. The top-level JSON object contains:

- **Databases**: An array of database configuration objects.

---

## Database Configuration Object

Each entry in the **Databases** array represents a database and includes the following properties:

- **Name** (string, *required*):  
  The unique name of the database configuration. This name is later used to select a specific database.

- **CsType** (string, *optional*):  
  The C# type name to be used when generating database classes. If not specified, the value of `Name` is used by default.

- **Namespace** (string, *optional*):  
  The C# namespace for generated models. Defaults to `"Models"` if not provided.

- **SourceDirectories** (array of strings, *optional*):  
  A list of directories where the source model files are located. These paths are used during model generation.

- **DestinationDirectory** (string, *optional*):  
  The output directory for generated model files.

- **Include** (array of strings, *optional*):  
  A filter list specifying which tables and views to include when generating models. If this list is omitted or left empty, DataLinq will include all tables and views found in the database schema.

- **UseRecord** (boolean, *optional*):  
  Determines whether generated models should use C# record types. Defaults to `false`.

- **UseFileScopedNamespaces** (boolean, *optional*):  
  When set to true, the generated code will use file-scoped namespaces (available in C# 10+).

- **UseNullableReferenceTypes** (boolean, *optional*):  
  Enables nullable reference types in the generated code.

- **CapitalizeNames** (boolean, *optional*):  
  If true, property names and other generated identifiers will be capitalized.

- **RemoveInterfacePrefix** (boolean, *optional*):  
  When true (the default), any leading "I" on interface names is removed during code generation.

- **SeparateTablesAndViews** (boolean, *optional*):  
  Indicates whether generated files should be placed in separate folders based on whether they represent tables or views.

- **Connections** (array, *required*):  
  An array of connection objects (see below) that specify how to connect to the database.

- **FileEncoding** (string, *required*):  
  The encoding to use when reading/writing files (for example, `"UTF8"` or `"UTF8BOM"`). If omitted, UTF-8 without BOM is used by default.  

---

## Connection Configuration Object

Each connection object (found in the **Connections** array) defines how to connect to the database. Its properties include:

- **Type** (string, *required*):  
  A string that identifies the type of database connection. This value is parsed to match a supported database provider (for example, `"MySQL"` or `"SQLite"`).  

- **DatabaseName** (string, *optional*):  
  An alternative name for the database; if not provided, the value of `DataSourceName` is used.

- **DataSourceName** (string, *required*):  
  The primary name for the data source. Depending on the connection type, this might represent a server name, file name, or other identifier.

- **ConnectionString** (string, *required*):  
  The full connection string used to establish a connection with the database.

---

## Merging datalinq.user.json

When DataLinq reads the configuration using the `DataLinqConfig.FindAndReadConfigs` method citeturn1file0, it:
  
1. Reads the main `datalinq.json` file.
2. Checks for a corresponding `datalinq.user.json` file (by replacing the extension).
3. Merges the settings from the user file into the main configuration. In this process, for any matching database (by name), properties in the user file override those in the main file. For example, if `CapitalizeNames` or the list of `Connections` are specified in the user file, those values will replace or augment the main configuration.

---

## Example: datalinq.json

Below is a simplified example of a `datalinq.json` file:

```json
{
  "Databases": [
    {
      "Name": "MyDatabase",
      "CsType": "MyDatabase",
      "Namespace": "MyApp.Models",
      "SourceDirectories": [ "Models/Source" ],
      "DestinationDirectory": "Models/Generated",
      "Include": [ "Users", "Orders", "ActiveUsers" ], // To include all tables and views, just omit "Include" entirely or leave the array empty.
      "UseRecord": true,
      "UseFileScopedNamespaces": false,
      "UseNullableReferenceTypes": true,
      "CapitalizeNames": true,
      "RemoveInterfacePrefix": true,
      "SeparateTablesAndViews": false,
      "FileEncoding": "UTF8"
    }
  ]
}
```

---

## Example: datalinq.user.json

A `datalinq.user.json` file may override or extend the main settings. For example:

```json
{
  "Databases": [
    {
      "Name": "MyDatabase",
      "CapitalizeNames": false,
      "Connections": [
        {
          "Type": "SQLite",
          "DataSourceName": "MyDatabase.db",
          "ConnectionString": "Data Source=MyDatabase.db;Cache=Shared;"
        }
      ]
    }
  ]
}
```

In this example, for the database named "MyDatabase", the user-specific file turns off name capitalization and provides a connection using SQLite. During initialization, these settings will be merged with the ones from the main file.

---

## Summary

- The **datalinq.json** file is the main configuration file and defines an array of databases with their settings. This file should be checked in to source control.
- **datalinq.user.json** is an optional file that overrides or extends settings from datalinq.json, allowing local or user-specific configuration changes, like connections strings and secret passwords. **This file should typically not be checked in to source control.**