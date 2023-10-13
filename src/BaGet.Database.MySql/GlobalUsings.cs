// Global using directives

global using BaGet.Core;
global using BaGet.Database.MySql;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata;
global using Microsoft.EntityFrameworkCore.Migrations;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;

#if !NETCOREAPP3_1
global using MySqlConnector;
#endif