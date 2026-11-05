# Project Template Local Development and Maintenance

## Updating project template JavaScript dependencies

The AIChatWeb project template within Microsoft.Extensions.AI.Templates bundles JavaScript dependencies into the package. To update those project template JavaScript dependencies:

1. Navigate into the root of the Microsoft.Extensions.AI.Templates directory
1. Install a recent build of Node.js
2. Update the `package.json` file with added or updated dependencies
3. Run the following commands:
    ```sh
    ../../scripts/UpdateNpmDependencies.ps1
    npm run copy-dependencies
    ```

To add a new dependency, run `npm install <package-name>` and update the `scripts` section in `package.json` to specify how the new dependency should be copied into its template.

## Component governance

There are two types of template dependencies that need to get scanned for component governance (CG):
* .NET dependencies (specified via `<PackageReference />` in each `.csproj` file)
* JS dependencies (everything in the `wwwroot/lib` folder of the `.Web` project)

There are template execution tests in the `test/ProjectTemplates` folders that create, restore, and build each possible variation of the project templates. These tests execute before the CG step of the internal CI pipeline, which scans the build artifacts from each generated project (namely the `project.assets.json` file and the local NuGet package cache) to detect which .NET dependencies got pulled in.

However, CG can't detect JS dependencies by scanning execution test output, because the generated projects don't contain manifests describing JS dependencies. Instead, we have a `package.json` and `package-lock.json` in the same folder as this README that define which JS dependencies get included in the template and how they get copied into template content (see previous section in this document). CG then automatically tracks packages listed in this `package-lock.json`.

## Build the templates

By default the templates use just-built versions of library packages from this repository, so NuGet packages must be produced before the templates can be run:

```pwsh
.\build.cmd -vs AI -noLaunch # Generate an SDK.sln for projects matching the AI filter
.\build.cmd -build -pack     # Build a NuGet package for each project in the generated SDK.sln
```

Once the library packages are built, the template packages can be built with references to the local package versions using the following commands:

```pwsh
.\build.cmd -build -pack -projects .\src\ProjectTemplates\Microsoft.Agents.AI.ProjectTemplates\Microsoft.Agents.AI.ProjectTemplates.csproj
.\build.cmd -build -pack -projects .\src\ProjectTemplates\Microsoft.Extensions.AI.Templates\Microsoft.Extensions.AI.Templates.csproj
```

## Package references in the project templates

The `Directory.Build.targets` file defines and configures the necessary targets for processing package references in the project template to insert the package versions into the produced `.csproj` files.

Template projects can reference packages either with pinned `<PackageVersion />` or have versions resolved from projects within the repository using `<ProjectReference />` items. If both a `<ProjectReference />` and `<PackageVersion />` are specified, the version from
the `<PackageVersion />` is used.

```xml
<!--
    Define a package version to be resolved in the template project directly.
    This also ensures the project is built in the template project's dependencies.
-->
<ProjectReference Include="$(SrcLibrariesDir)Microsoft.Extensions.AI\Microsoft.Extensions.AI.csproj" />

<!--
    Pin a project's package version to an already released version from the
    template project directly or in /eng/packages/ProjectTemplates.props.
-->
<PackageVersion Include="Microsoft.Extensions.DataIngestion" Version="10.0.1-preview.1.25571.5" />

<!-- Define a package version for an external dependency in /eng/packages/ProjectTemplates.props -->
<PackageVersion Include="Azure.AI.Projects" Version="1.1.0" />

<!--
    Override a package version for an external dependency in the template project directly
    when the package version is defined in a broader props file but needs to be overridden
    specifically in the project template package.
-->
<PackageVersion Update="OllamaSharp" Version="5.4.9" />
```

Files in the template that need to reference the resolved versions must be pre-processed as generated template content. While this works with any text file, it is typically used with `.csproj` files. This is accomplished by:

1. Rename the `.csproj` file to `.csproj-in`
2. Exclude the `.csproj-in` file from the project `<Content />`
3. Include the `.csproj-in` file in the project's `<TemplateContent />`

**Project template project**
```xml
  <ItemGroup>
    <Compile Remove="**\*" />

    <Content
      Include="templates\**\*"
      Exclude="templates\**\*.csproj-in"
      PackagePath="content" />

    <TemplateContent
      Include="templates\**\*.csproj-in"
      ChangeExtension=".csproj"
      PackagePath="content" />
  </ItemGroup>
```

Note that for the 'ChangeExtension' behavior to work, the extension to be replaced cannot contain a '.'. Therefore, `.csproj.in` cannot be used, which is why `.csproj-in` is used instead. This is true for any extension, so `.md-in` can also be changed to `.md` using this same logic.

**Project template csproj-in file**
```xml
<Sdk Name="Aspire.AppHost.Sdk" Version="${PackageVersion:Aspire}" />
...
<PackageReference
    Include="Microsoft.Extensions.AI"
    Version="${PackageVersion:Microsoft.Extensions.AI}" />
...
<PackageReference
    Include="Aspire.Hosting.AppHost"
    Version="${PackageVersion:Aspire}" />
...
<PackageReference
    Include="Aspire.Azure.AI.OpenAI"
    Version="${PackageVersion:Aspire-Preview}" />
```

During build, the generated content is saved into the `/artifacts/ProjectTemplates/GeneratedContent` folder, and the generated content is then added as `<Content />` included from the artifacts folder.

## Installing the templates locally

After building the templates package using one of the approaches above, it can be installed locally. **Note:** Since package versions don't change between local builds, the recommended steps include clearing the `Microsoft.Extensions.AI*` and `Microsoft.Agents.AI*` packages from your local nuget cache.

**Note:** For the following commands to succeed, you'll need to either install a compatible .NET SDK globally or prepend the repo's generated `.dotnet` folder to the PATH environment variable.

```pwsh
# Uninstall any existing version of the templates
dotnet new uninstall Microsoft.Agents.AI.ProjectTemplates
dotnet new uninstall Microsoft.Extensions.AI.Templates

# Clear the packages from the NuGet cache since the local package version does not change
Remove-Item ~\.nuget\packages\Microsoft.Agents.AI* -Recurse -Force
Remove-Item ~\.nuget\packages\Microsoft.Extensions.AI* -Recurse -Force

# Install the templates from the generated .nupkg file (in the artifacts/packages folder)
dotnet new install .\artifacts\packages\Debug\Shipping\Microsoft.Agents.AI.ProjectTemplates*.nupkg
dotnet new install .\artifacts\packages\Debug\Shipping\Microsoft.Extensions.AI.Templates*.nupkg
```

Finally, create a project from the template and run it:

```pwsh
    dotnet new aiagent-webapi `
        --provider <azureopenai | ollama | openai> `
        [--managed-identity]

# or

    dotnet new aichatweb `
        --provider <azureopenai | ollama | openai> `
        [--vector-store <azureaisearch | local | qdrant>] `
        [--aspire] `
        [--managed-identity]

    # If using `--aspire`, cd into the *AppHost directory
    # Follow the instructions in the generated README for setting the necessary user-secrets

dotnet run
```

## Cleaning ProjectTemplate build output

Running the `clean` target for a Project Template project will remove its entire artifacts folder. This includes:

- `/artifacts/ProjectTemplates/<package-name>/GeneratedContent` (generated template content files)
- `/artifacts/ProjectTemplates/<package-name>/Sandbox` (execution test sandbox)
- `/artifacts/ProjectTemplates/<package-name>/Snapshots` (snapshot test output)
