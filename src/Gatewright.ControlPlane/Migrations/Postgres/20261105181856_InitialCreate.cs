// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Gatewright.ControlPlane.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Apis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    BasePath = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: true),
                    Backend = table.Column<string>(type: "text", nullable: false),
                    Policies = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<long>(type: "bigint", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutoImports",
                columns: table => new
                {
                    ServiceRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DocumentHash = table.Column<string>(type: "text", nullable: false),
                    ImportedAt = table.Column<long>(type: "bigint", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoImports", x => x.ServiceRef);
                });

            migrationBuilder.CreateTable(
                name: "Consumers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Contact = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consumers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StateJson = table.Column<string>(type: "text", nullable: false),
                    EndpointsHash = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Environments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrentPublicationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Environments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Hostnames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Certificate = table.Column<string>(type: "text", nullable: true),
                    RedirectHttpToHttps = table.Column<bool>(type: "boolean", nullable: false),
                    Hsts = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hostnames", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nodes",
                columns: table => new
                {
                    NodeId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    AppliedVersion = table.Column<long>(type: "bigint", nullable: false),
                    AppliedHash = table.Column<string>(type: "text", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    Connected = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedAt = table.Column<long>(type: "bigint", nullable: false),
                    LastSeen = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nodes", x => x.NodeId);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequiresSubscription = table.Column<bool>(type: "boolean", nullable: false),
                    Policies = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    ConfigHash = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Revisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScopePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Global = table.Column<string>(type: "text", nullable: false),
                    Gateway = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopePolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SharedPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Definition = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Method = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Policies = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operations_Apis_ApiId",
                        column: x => x.ApiId,
                        principalTable: "Apis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Plans_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductApi",
                columns: table => new
                {
                    ApisId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductApi", x => new { x.ApisId, x.ProductsId });
                    table.ForeignKey(
                        name: "FK_ProductApi_Apis_ApisId",
                        column: x => x.ApisId,
                        principalTable: "Apis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductApi_Products_ProductsId",
                        column: x => x.ProductsId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Publications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    SnapshotHash = table.Column<string>(type: "text", nullable: false),
                    PublishedBy = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Publications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Publications_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Publications_Revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "Revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    RevokedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionKeys_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Apis_Name",
                table: "Apis",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Timestamp",
                table: "AuditEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Consumers_Name",
                table: "Consumers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Environments_Name",
                table: "Environments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hostnames_Name",
                table: "Hostnames",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_Environment",
                table: "Nodes",
                column: "Environment");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_ApiId_Name",
                table: "Operations",
                columns: new[] { "ApiId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plans_ProductId_Name",
                table: "Plans",
                columns: new[] { "ProductId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductApi_ProductsId",
                table: "ProductApi",
                column: "ProductsId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Publications_EnvironmentId_Number",
                table: "Publications",
                columns: new[] { "EnvironmentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Publications_RevisionId",
                table: "Publications",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Revisions_Number",
                table: "Revisions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedPolicies_Name",
                table: "SharedPolicies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionKeys_Hash",
                table: "SubscriptionKeys",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionKeys_SubscriptionId",
                table: "SubscriptionKeys",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ConsumerId",
                table: "Subscriptions",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_Name",
                table: "Subscriptions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ProductId",
                table: "Subscriptions",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "AutoImports");

            migrationBuilder.DropTable(
                name: "DiscoveryStates");

            migrationBuilder.DropTable(
                name: "Hostnames");

            migrationBuilder.DropTable(
                name: "Nodes");

            migrationBuilder.DropTable(
                name: "Operations");

            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.DropTable(
                name: "ProductApi");

            migrationBuilder.DropTable(
                name: "Publications");

            migrationBuilder.DropTable(
                name: "ScopePolicies");

            migrationBuilder.DropTable(
                name: "SharedPolicies");

            migrationBuilder.DropTable(
                name: "SubscriptionKeys");

            migrationBuilder.DropTable(
                name: "Apis");

            migrationBuilder.DropTable(
                name: "Environments");

            migrationBuilder.DropTable(
                name: "Revisions");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Consumers");

            migrationBuilder.DropTable(
                name: "Products");
        }
    }
}
