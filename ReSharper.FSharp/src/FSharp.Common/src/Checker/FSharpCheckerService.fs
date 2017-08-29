﻿namespace JetBrains.ReSharper.Plugins.FSharp.Common.Checker

open System
open System.Collections.Generic
open System.Linq
open System.Threading
open JetBrains.Annotations
open JetBrains.Application
open JetBrains.Application.Threading
open JetBrains.DataFlow
open JetBrains.ProjectModel
open JetBrains.ReSharper.Feature.Services
open JetBrains.ReSharper.Psi
open JetBrains.ReSharper.Psi.Tree
open JetBrains.ProjectModel.Properties
open JetBrains.ProjectModel.Properties.CSharp
open JetBrains.ProjectModel.Properties.Managed
open JetBrains.ReSharper.Plugins.FSharp.Common.Util
open JetBrains.ReSharper.Plugins.FSharp.ProjectModelBase
open JetBrains.ReSharper.Plugins.FSharp.ProjectModel.ProjectProperties
open JetBrains.ReSharper.Psi.Modules
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices

type FSharpParseAndCheckResults = 
    {
      ParseResults: FSharpParseFileResults
      ParseTree: Ast.ParsedInput
      CheckResults: FSharpCheckFileResults
    }

[<ShellComponent>]
type FSharpCheckerService(lifetime, onSolutionCloseNotifier: OnSolutionCloseNotifier) =
    let checker = lazy FSharpChecker.Create(projectCacheSize = 200, keepAllBackgroundResolutions = false,
                                       legacyReferenceResolver = MSBuildReferenceResolver.Resolver)
    do
        onSolutionCloseNotifier.SolutionIsAboutToClose.Advise(lifetime, fun () ->
            checker.Value.ClearLanguageServiceRootCachesAndCollectAndFinalizeAllTransients())

    member val OptionsProvider: IFSharpProjectOptionsProvider = null with get, set
    member x.Checker = checker.Value

    member x.ParseFile([<NotNull>] file: IPsiSourceFile) =
        match x.OptionsProvider.GetParsingOptions(file, x.Checker) with
        | Some parsingOptions as options ->
            let filePath = file.GetLocation().FullPath
            let source = file.Document.GetText()
            options, Some (x.Checker.ParseFile(filePath, source, parsingOptions).RunAsTask())
        | _ -> None, None

    member x.HasPairFile([<NotNull>] file: IPsiSourceFile) =
        x.OptionsProvider.HasPairFile(file, x.Checker)

    member x.GetDefines(sourceFile: IPsiSourceFile) =
        if sourceFile.LanguageType.Equals(FSharpScriptProjectFileType.Instance) ||
            sourceFile.PsiModule.IsMiscFilesProjectModule() then []
        else
            match x.OptionsProvider.TryGetFSharpProject(sourceFile, x.Checker) with
            | Some project -> project.ConfigurationDefines
            | _ -> List.empty

    member x.GetParsingOptions(sourceFile: IPsiSourceFile) =
        x.OptionsProvider.GetParsingOptions(sourceFile, x.Checker)

    member x.ParseAndCheckFile([<NotNull>] file: IPsiSourceFile, allowStaleResults: bool) =
        match x.OptionsProvider.GetProjectOptions(file, x.Checker, true) with
        | Some options ->
            let filePath = file.GetLocation().FullPath
            let source = file.Document.GetText()
            let parsingResults, checkResults = x.Checker.ParseAndCheckFileInProject(filePath, 0, source, options).RunAsTask()
            match parsingResults.ParseTree, checkResults with
            | Some parseTree, FSharpCheckFileAnswer.Succeeded checkResults ->
                Some { ParseResults = parsingResults; ParseTree = parseTree; CheckResults = checkResults }
            | _ -> None
        | _ -> None