// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------
#r "paket: groupref Tools //"
#load "./.fake/build.fsx/intellisense.fsx"
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.DotNet
open Fake.Core
open Fake.Tools.Git.CommandHelper
open Fake.Tools.Git
open Fake.DotNet.Testing.XUnit2
open Fake.Testing.Common
open System.Linq

let project = "BaGet"
// Pattern specifying assemblies to be tested using XUnit
let testAssemblies = !! "tests/*.Tests/bin/Release/*/publish/*.Tests.dll"

// Read additional information from the release notes document
let release = File.read "CHANGELOG.md" |> ReleaseNotes.parse
let changelogVersion = release.AssemblyVersion

// --------------------------------------------------------------------------------------
// Build projects
    
Target.create "Build" (fun _ ->
    let msbuild target =
        let setParams (defaults:MSBuildParams) =
            { defaults with
                Verbosity = Some(Quiet)
                Targets = [target]
                Properties =
                    [
                        "Version", changelogVersion
                        "Configuration", "Release"
                    ]
             }
        MSBuild.build setParams ""
    msbuild "Restore"
    msbuild "Publish"
)

// Build SPA
let spaRoot = "src" </> "BaGet.UI"
let spaPublishDir = "src" </> "BaGet" </> "bin" </> "Release" </> "netcoreapp2.1" </> "publish" </> "wwwroot"

let runSpaProcess exe args timeout =
    let result =
        Process.execWithResult (fun info ->
        { info with
            FileName = exe
            WorkingDirectory = spaRoot
            Arguments = args}) timeout
    if result.ExitCode <> 0 then
        let errors = System.String.Join(System.Environment.NewLine,result.Errors)
        Trace.traceError <| System.String.Join(System.Environment.NewLine,result.Messages)
        failwithf "%s process exited with %d: %s" exe result.ExitCode errors
    Trace.trace <| System.String.Join(System.Environment.NewLine,result.Messages)

Target.create "SpaRestore" (fun _ -> 
    runSpaProcess "yarn" "install"  (System.TimeSpan.FromMinutes(10.0))
)

Target.create "SpaBuild" (fun _ -> 
    runSpaProcess "npm" "run build"  (System.TimeSpan.FromMinutes(2.0))
)

Target.create "SpaPublish" (fun _ -> 
    Directory.delete spaPublishDir
    Directory.create spaPublishDir
    !! "src/BaGet.UI/dist/**/*" |> Shell.copy spaPublishDir
)

// --------------------------------------------------------------------------------------
// Run the unit tests 

let runXunit setParams assemblies =
    // A bit of patching to use dotnet core runner
    let discoverNoAppDomainExists parameters =
        let helpText =
            Process.execWithResult ((fun info ->
                { info with FileName = parameters.ToolPath}) >> Process.withFramework) (System.TimeSpan.FromMinutes 1.)
        let canSetNoAppDomain = helpText.Messages.Any(fun msg -> msg.Contains("-noappdomain"))
        {parameters with NoAppDomain = canSetNoAppDomain}
    let (|OK|Failure|) = function
        | 0 -> OK
        | x -> Failure x

    let buildErrorMessage = function
        | OK -> None
        | Failure errorCode ->
            Some (sprintf "xUnit2 reported an error (Error Code %d)" errorCode)

    let failBuildWithMessage = function
        | DontFailBuild -> Trace.traceImportant
        | _ -> (fun m -> raise(FailedTestsException m))

    let failBuildIfXUnitReportedError errorLevel =
        buildErrorMessage
        >> Option.iter (failBuildWithMessage errorLevel)
    let details = String.separated ", " assemblies
    use __ = Trace.traceTask "xUnit2" details
    let parametersFirst = setParams XUnit2Defaults

    let parameters =
        if parametersFirst.NoAppDomain
        then discoverNoAppDomainExists parametersFirst
        else parametersFirst

    let xunitRunner = "packages/tools/xunit.runner.console/tools/netcoreapp2.0/xunit.console.dll"
    let args = xunitRunner + " " + buildArgs parameters assemblies    

    let result =
        Process.execSimple ((fun info ->
        { info with
            FileName = "dotnet"
            WorkingDirectory = defaultArg parameters.WorkingDir "."
            Arguments = args}) >> Process.withFramework) parameters.TimeOut

    failBuildIfXUnitReportedError parameters.ErrorLevel result
    __.MarkSuccess()

let runDotnet options command args =
    let result = DotNet.exec options command args
    if result.ExitCode <> 0 then
        let errors = System.String.Join(System.Environment.NewLine,result.Errors)
        Trace.traceError <| System.String.Join(System.Environment.NewLine,result.Messages)
        failwithf "dotnet process exited with %d: %s" result.ExitCode errors

Target.create "RunUnitTests" (fun _ -> 
    testAssemblies 
    |> runXunit (fun p -> {
            p with 
                ExcludeTraits=[ ("Category","integration") ] 
                TimeOut=System.TimeSpan.FromMinutes(5.0)
                XmlOutputPath=Some "UnitTestsResults.xml"
                HtmlOutputPath=Some "UnitTestResults.html"
        })
)

Target.create "RunIntegrationTests" (fun _ -> 
    testAssemblies 
    |> runXunit (fun p -> {
            p with 
                IncludeTraits=[ ("Category","integration") ] 
        })
)

open Fake.Core.TargetOperators

Target.create "All" ignore

"SpaRestore" 
    ==> "SpaBuild"
    ==> "SpaPublish"
    ==> "All"

"Build"
  ==> "RunUnitTests"
  ==> "RunIntegrationTests"
  ==> "All"
// start build
Target.runOrDefault "All"