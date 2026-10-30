using System.Collections.Generic;

namespace BaGet.Identity.Application.Screens;

/// <summary>
/// Every screen of the web UI, by the controller and default GET action that serve it (see <see cref="ScreenRef"/>). Use cases name the screen
/// they continue at with these members; a test checks that each one still resolves to an existing view action.
/// </summary>
public static class ScreenCatalog
{
    /// <summary>AccountPackages/Access.</summary>
    public static readonly ScreenRef AccountAccess = new("AccountPackages", "Access");

    /// <summary>AccountSettings/ApiKeysIndex.</summary>
    public static readonly ScreenRef AccountApiKeys = new("AccountSettings", "ApiKeysIndex");

    /// <summary>Account/ConfirmEmail.</summary>
    public static readonly ScreenRef AccountConfirmEmail = new("Account", "ConfirmEmail");

    /// <summary>Account/ForgotPassword.</summary>
    public static readonly ScreenRef AccountForgotPassword = new("Account", "ForgotPassword");

    /// <summary>Account/Login.</summary>
    public static readonly ScreenRef AccountLogin = new("Account", "Login");

    /// <summary>Account/LoginWith2fa.</summary>
    public static readonly ScreenRef AccountLoginWith2fa = new("Account", "LoginWith2fa");

    /// <summary>Account/LoginWithRecoveryCode.</summary>
    public static readonly ScreenRef AccountLoginWithRecoveryCode = new("Account", "LoginWithRecoveryCode");

    /// <summary>Account/Logout.</summary>
    public static readonly ScreenRef AccountLogout = new("Account", "Logout");

    /// <summary>AccountOrganizations/Organizations.</summary>
    public static readonly ScreenRef AccountOrganizations = new("AccountOrganizations", "Organizations");

    /// <summary>AccountPackages/Packages.</summary>
    public static readonly ScreenRef AccountPackages = new("AccountPackages", "Packages");

    /// <summary>AccountPackages/MyProfile.</summary>
    public static readonly ScreenRef AccountProfile = new("AccountPackages", "MyProfile");

    /// <summary>Account/Reauthenticate.</summary>
    public static readonly ScreenRef AccountReauthenticate = new("Account", "Reauthenticate");

    /// <summary>Account/Register.</summary>
    public static readonly ScreenRef AccountRegister = new("Account", "Register");

    /// <summary>Account/ResetPassword.</summary>
    public static readonly ScreenRef AccountResetPassword = new("Account", "ResetPassword");

    /// <summary>AccountSettings/Settings.</summary>
    public static readonly ScreenRef AccountSettings = new("AccountSettings", "Settings");

    /// <summary>AccountSettings/TwoFactor.</summary>
    public static readonly ScreenRef AccountTwoFactor = new("AccountSettings", "TwoFactor");

    /// <summary>AdminOperations/Abuse.</summary>
    public static readonly ScreenRef AdminAbuse = new("AdminOperations", "Abuse");

    /// <summary>AdminOperations/AccessUnavailable.</summary>
    public static readonly ScreenRef AdminAccessUnavailable = new("AdminOperations", "AccessUnavailable");

    /// <summary>ApiCatalog/Index.</summary>
    public static readonly ScreenRef AdminApi = new("ApiCatalog", "Index");

    /// <summary>AdminAudit/Index.</summary>
    public static readonly ScreenRef AdminAudit = new("AdminAudit", "Index");

    /// <summary>AdminOperations/Cluster.</summary>
    public static readonly ScreenRef AdminCluster = new("AdminOperations", "Cluster");

    /// <summary>AdminUsers/DeleteUser.</summary>
    public static readonly ScreenRef AdminDeleteUser = new("AdminUsers", "DeleteUser");

    /// <summary>AdminOperations/Formats.</summary>
    public static readonly ScreenRef AdminFormats = new("AdminOperations", "Formats");

    /// <summary>AdminGroups/Detail.</summary>
    public static readonly ScreenRef AdminGroupDetail = new("AdminGroups", "Detail");

    /// <summary>AdminGroups/Index.</summary>
    public static readonly ScreenRef AdminGroups = new("AdminGroups", "Index");

    /// <summary>AdminOperations/Index.</summary>
    public static readonly ScreenRef AdminOperations = new("AdminOperations", "Index");

    /// <summary>AdminOrganizations/Detail.</summary>
    public static readonly ScreenRef AdminOrganizationDetail = new("AdminOrganizations", "Detail");

    /// <summary>AdminOrganizations/Index.</summary>
    public static readonly ScreenRef AdminOrganizations = new("AdminOrganizations", "Index");

    /// <summary>AdminPackages/Index.</summary>
    public static readonly ScreenRef AdminPackages = new("AdminPackages", "Index");

    /// <summary>AdminPermissions/Detail.</summary>
    public static readonly ScreenRef AdminPermissionDetail = new("AdminPermissions", "Detail");

    /// <summary>AdminPermissions/Index.</summary>
    public static readonly ScreenRef AdminPermissions = new("AdminPermissions", "Index");

    /// <summary>AdminProjects/Detail.</summary>
    public static readonly ScreenRef AdminProjectDetail = new("AdminProjects", "Detail");

    /// <summary>AdminProjects/Index.</summary>
    public static readonly ScreenRef AdminProjects = new("AdminProjects", "Index");

    /// <summary>AdminReplication/Index.</summary>
    public static readonly ScreenRef AdminReplication = new("AdminReplication", "Index");

    /// <summary>AdminReplication/Config.</summary>
    public static readonly ScreenRef AdminReplicationConfig = new("AdminReplication", "Config");

    /// <summary>AdminRepositories/Index.</summary>
    public static readonly ScreenRef AdminRepositories = new("AdminRepositories", "Index");

    /// <summary>AdminRepositories/DeleteRepository.</summary>
    public static readonly ScreenRef AdminRepositoryDelete = new("AdminRepositories", "DeleteRepository");

    /// <summary>AdminRepositories/Edit.</summary>
    public static readonly ScreenRef AdminRepositoryEdit = new("AdminRepositories", "Edit");

    /// <summary>AdminRetention/Index.</summary>
    public static readonly ScreenRef AdminRetention = new("AdminRetention", "Index");

    /// <summary>AdminRetention/Holds.</summary>
    public static readonly ScreenRef AdminRetentionHolds = new("AdminRetention", "Holds");

    /// <summary>AdminRetention/Policy.</summary>
    public static readonly ScreenRef AdminRetentionPolicy = new("AdminRetention", "Policy");

    /// <summary>AdminScanning/Project.</summary>
    public static readonly ScreenRef AdminScanProject = new("AdminScanning", "Project");

    /// <summary>AdminScanning/Repository.</summary>
    public static readonly ScreenRef AdminScanRepository = new("AdminScanning", "Repository");

    /// <summary>AdminScanning/Index.</summary>
    public static readonly ScreenRef AdminScanning = new("AdminScanning", "Index");

    /// <summary>AdminSettings/Index.</summary>
    public static readonly ScreenRef AdminSettings = new("AdminSettings", "Index");

    /// <summary>AdminUsers/Detail.</summary>
    public static readonly ScreenRef AdminUserDetail = new("AdminUsers", "Detail");

    /// <summary>AdminUsers/UserPackages.</summary>
    public static readonly ScreenRef AdminUserPackages = new("AdminUsers", "UserPackages");

    /// <summary>AdminUsers/Index.</summary>
    public static readonly ScreenRef AdminUsers = new("AdminUsers", "Index");

    /// <summary>AdminOperations/Validation.</summary>
    public static readonly ScreenRef AdminValidation = new("AdminOperations", "Validation");

    /// <summary>DockerBrowse/Image.</summary>
    public static readonly ScreenRef DockerImage = new("DockerBrowse", "Image");

    /// <summary>DockerBrowse/Images.</summary>
    public static readonly ScreenRef DockerImages = new("DockerBrowse", "Images");

    /// <summary>Home/Index.</summary>
    public static readonly ScreenRef Index = new("Home", "Index");

    /// <summary>MavenBrowse/Artifact.</summary>
    public static readonly ScreenRef MavenArtifact = new("MavenBrowse", "Artifact");

    /// <summary>MavenBrowse/Artifacts.</summary>
    public static readonly ScreenRef MavenArtifacts = new("MavenBrowse", "Artifacts");

    /// <summary>MavenBrowse/Upload.</summary>
    public static readonly ScreenRef MavenUpload = new("MavenBrowse", "Upload");

    /// <summary>NpmBrowse/Package.</summary>
    public static readonly ScreenRef NpmPackage = new("NpmBrowse", "Package");

    /// <summary>NpmBrowse/Packages.</summary>
    public static readonly ScreenRef NpmPackages = new("NpmBrowse", "Packages");

    /// <summary>NpmBrowse/Upload.</summary>
    public static readonly ScreenRef NpmUpload = new("NpmBrowse", "Upload");

    /// <summary>Package/Show.</summary>
    public static readonly ScreenRef Package = new("Package", "Show");

    /// <summary>ContactOwners/Index.</summary>
    public static readonly ScreenRef PackagesContactOwners = new("ContactOwners", "Index");

    /// <summary>DockerManage/Index.</summary>
    public static readonly ScreenRef PackagesDockerManage = new("DockerManage", "Index");

    /// <summary>PackageManage/Index.</summary>
    public static readonly ScreenRef PackagesManage = new("PackageManage", "Index");

    /// <summary>MavenManage/Index.</summary>
    public static readonly ScreenRef PackagesMavenManage = new("MavenManage", "Index");

    /// <summary>NpmManage/Index.</summary>
    public static readonly ScreenRef PackagesNpmManage = new("NpmManage", "Index");

    /// <summary>ScanArtifact/Index.</summary>
    public static readonly ScreenRef PackagesScan = new("ScanArtifact", "Index");

    /// <summary>ValidationStatusPage/Index.</summary>
    public static readonly ScreenRef PackagesValidationStatus = new("ValidationStatusPage", "Index");

    /// <summary>Legal/Privacy.</summary>
    public static readonly ScreenRef Privacy = new("Legal", "Privacy");

    /// <summary>Profile/Profile.</summary>
    public static readonly ScreenRef ProfilesProfile = new("Profile", "Profile");

    /// <summary>ReportAbuse/Show.</summary>
    public static readonly ScreenRef ReportAbuse = new("ReportAbuse", "Show");

    /// <summary>Statistics/Index.</summary>
    public static readonly ScreenRef Statistics = new("Statistics", "Index");

    /// <summary>Legal/Terms.</summary>
    public static readonly ScreenRef Terms = new("Legal", "Terms");

    /// <summary>Upload/Index.</summary>
    public static readonly ScreenRef Upload = new("Upload", "Index");

    /// <summary>Every screen above.</summary>
    public static IReadOnlyList<ScreenRef> All { get; } = new[]
    {
        AccountAccess,
        AccountApiKeys,
        AccountConfirmEmail,
        AccountForgotPassword,
        AccountLogin,
        AccountLoginWith2fa,
        AccountLoginWithRecoveryCode,
        AccountLogout,
        AccountOrganizations,
        AccountPackages,
        AccountProfile,
        AccountReauthenticate,
        AccountRegister,
        AccountResetPassword,
        AccountSettings,
        AccountTwoFactor,
        AdminAbuse,
        AdminAccessUnavailable,
        AdminApi,
        AdminAudit,
        AdminCluster,
        AdminDeleteUser,
        AdminFormats,
        AdminGroupDetail,
        AdminGroups,
        AdminOperations,
        AdminOrganizationDetail,
        AdminOrganizations,
        AdminPackages,
        AdminPermissionDetail,
        AdminPermissions,
        AdminProjectDetail,
        AdminProjects,
        AdminReplication,
        AdminReplicationConfig,
        AdminRepositories,
        AdminRepositoryDelete,
        AdminRepositoryEdit,
        AdminRetention,
        AdminRetentionHolds,
        AdminRetentionPolicy,
        AdminScanProject,
        AdminScanRepository,
        AdminScanning,
        AdminSettings,
        AdminUserDetail,
        AdminUserPackages,
        AdminUsers,
        AdminValidation,
        DockerImage,
        DockerImages,
        Index,
        MavenArtifact,
        MavenArtifacts,
        MavenUpload,
        NpmPackage,
        NpmPackages,
        NpmUpload,
        Package,
        PackagesContactOwners,
        PackagesDockerManage,
        PackagesManage,
        PackagesMavenManage,
        PackagesNpmManage,
        PackagesScan,
        PackagesValidationStatus,
        Privacy,
        ProfilesProfile,
        ReportAbuse,
        Statistics,
        Terms,
        Upload,
    };
}
