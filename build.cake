#addin Cake.Coveralls

#tool "nuget:?package=GitVersion.CommandLine"
#tool "nuget:?package=OpenCover"
#tool "nuget:?package=coveralls.io"

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var framework = Argument("framework", "netcoreapp1.1");

var outputDir = "./buildOutput/";
var artifactName = outputDir + "artifact.zip";
var coverageOutput = outputDir + "coverage.xml";

var projectPath = "./AspNetCore.CrudDemo";
var projectJsonPath = projectPath + "/project.json";

Task("Clean")
	.Does(() => 
	{
		if (DirectoryExists(outputDir))
			DeleteDirectory(outputDir, recursive:true);

		CreateDirectory(outputDir);
	});

Task("Restore")
	.Does(() => DotNetCoreRestore());

Task("Version")
	.Does(() => 
	{
		GitVersion(new GitVersionSettings
		{
			UpdateAssemblyInfo = true,
			OutputType = GitVersionOutput.BuildServer
		});

		var versionInfo = GitVersion(new GitVersionSettings{ OutputType = GitVersionOutput.Json });
		var updatedProjectJson = System.IO.File.ReadAllText(projectJsonPath)
			.Replace("1.0.0-*", versionInfo.NuGetVersion);
			
		System.IO.File.WriteAllText(projectJsonPath, updatedProjectJson);
	});

Task("Build")
	.IsDependentOn("Clean")
	.IsDependentOn("Restore")
	.IsDependentOn("Version")
	.Does(() => 
	{
		var projects = GetFiles("./**/*.xproj");

		var settings = new DotNetCoreBuildSettings
		{
			Framework = framework,
			Configuration = configuration,
		};

		foreach (var project in projects)
		{
			DotNetCoreBuild(project.GetDirectory().FullPath, settings);
		}
	});

Task("TestWithoutCoverage")
	.WithCriteria(() => BuildSystem.IsRunningOnTravisCI)
	.IsDependentOn("Build")
	.Does(() => 
	{
		DotNetCoreTest("./AspNetCore.CrudDemo.Controllers.Tests", new DotNetCoreTestSettings 
		{
			Framework = framework,
			Configuration = configuration
		});
	});

Task("TestWithCoverage")
	.WithCriteria(() => !BuildSystem.IsRunningOnTravisCI)
	.IsDependentOn("Build")
	.Does(() => 
	{
		TestWithCoverage("./AspNetCore.CrudDemo.Controllers.Tests", framework, configuration, coverageOutput, "+[AspNetCore.CrudDemo]AspNetCore.CrudDemo.Controllers.*");

		if (BuildSystem.IsLocalBuild)
			TestWithCoverage("./AspNetCore.CrudDemo.Services.Tests", framework, configuration, coverageOutput, "+[AspNetCore.CrudDemo]AspNetCore.CrudDemo.Services.*");
	});

private void TestWithCoverage(string testProject, string framework, string configuration, string coverageOutput, string filters)
{
	Action<ICakeContext> testAction = tool => 
	{
		tool.DotNetCoreTest(testProject, new DotNetCoreTestSettings 
		{
			Framework = framework,
			Configuration = configuration
		});
	};

	OpenCover(testAction, coverageOutput, new OpenCoverSettings 
	{
		// Needed for .NET Core
		OldStyle = true,
		MergeOutput = true,
		Register = "user",
		ArgumentCustomization = args => args.Append("-hideskipped:all")
	}.WithFilter(filters));
}

Task("CoverallsUpload")
	.WithCriteria(() => FileExists(coverageOutput))
	.WithCriteria(() => BuildSystem.IsRunningOnAppVeyor)
	.IsDependentOn("TestWithCoverage")	
	.Does(() => 
	{
		CoverallsIo(coverageOutput, new CoverallsIoSettings()
		{
			RepoToken = EnvironmentVariable("coveralls_repo_token")
		});
	});

Task("Publish")
	.IsDependentOn("TestWithCoverage")
	.IsDependentOn("TestWithoutCoverage")
	.Does(() => 
	{
		var settings = new DotNetCorePublishSettings
		{
			Configuration = configuration,
			OutputDirectory = outputDir
		};
					
		DotNetCorePublish(projectPath, settings);
		Zip(outputDir, artifactName);
	});

Task("AppVeyorUpload")
	.WithCriteria(() => BuildSystem.IsRunningOnAppVeyor)
	.IsDependentOn("Publish")
	.Does(() => 
	{
		var files = GetFiles(artifactName);
		foreach (var file in files)
			AppVeyor.UploadArtifact(file.FullPath);
	});

Task("Default")
	.IsDependentOn("AppVeyorUpload")
	.IsDependentOn("CoverallsUpload");

RunTarget(target);