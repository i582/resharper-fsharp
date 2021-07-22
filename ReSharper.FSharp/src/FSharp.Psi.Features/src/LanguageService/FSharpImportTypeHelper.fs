namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Features.LanguageService

open JetBrains.ProjectModel
open JetBrains.ReSharper.Intentions.QuickFixes
open JetBrains.ReSharper.Plugins.FSharp.Psi
open JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Daemon.QuickFixes
open JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Search
open JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Util
open JetBrains.ReSharper.Plugins.FSharp.Psi.Impl
open JetBrains.ReSharper.Plugins.FSharp.Psi.Resolve
open JetBrains.ReSharper.Plugins.FSharp.Psi.Tree
open JetBrains.ReSharper.Plugins.FSharp.Psi.Util
open JetBrains.ReSharper.Psi
open JetBrains.ReSharper.Psi.ExtensionsAPI.Finder
open JetBrains.ReSharper.Psi.Modules
open JetBrains.ReSharper.Resources.Shell

[<Language(typeof<FSharpLanguage>)>]
type FSharpImportTypeHelper() =
    let [<Literal>] opName = "FSharpImportTypeHelper.FindTypeCandidates"

    let isApplicable (context: IFSharpReferenceOwner) =
        let referenceName = context.As<ITypeReferenceName>()
        not (isNotNull (OpenStatementNavigator.GetByReferenceName(referenceName)))

    interface IImportTypeHelper with
        member x.FindTypeCandidates(reference, importTypeCacheFactory) =
            let reference = reference.As<FSharpSymbolReference>()
            if isNull reference || reference.IsQualified then Seq.empty else

            let context = reference.GetElement()
            if not (isApplicable context) then Seq.empty else

            let sourceFile = context.GetSourceFile()
            let psiModule = context.GetPsiModule()
            let containingModules = getContainingModules context

            let names = reference.GetAllNames().ResultingList()
            let factory = importTypeCacheFactory.Invoke(context)

            let canReferenceInsideProject typeElement =
                let searchGuru = psiModule.GetSolution().GetComponent<FSharpSearchGuru>() :> ISearchGuru
                let elementId = searchGuru.GetElementId(typeElement)
                searchGuru.CanContainReferences(sourceFile, elementId)

            let mutable candidates: ITypeElement seq =
                names
                |> Seq.collect factory.Invoke
                |> Seq.filter (fun clrDeclaredElement ->
                    let typeElement = clrDeclaredElement.As<ITypeElement>()
                    if isNull typeElement then false else

                    // todo: enable when singleton property cases are supported
                    if typeElement.IsUnionCase() then false else

                    if typeElement.Module == psiModule &&
                            not (canReferenceInsideProject typeElement) then false else

                    let moduleToOpen = getModuleToOpen typeElement
                    if containingModules.Contains(moduleToOpen) then false else

                    if typeElement.Module != psiModule &&
                            not (psiModule.References(typeElement.Module)) then
                        true else

                    let fsFile = context.FSharpFile
                    let names = toQualifiedList fsFile typeElement |> List.map (fun el -> el.GetSourceName())
                    let symbolUse = fsFile.CheckerService.ResolveNameAtLocation(context, names, opName)
                    Option.isSome symbolUse)
                |> Seq.cast


            let referenceName = context.As<ITypeArgumentOwner>()
            if isNotNull referenceName then
                let typeArgumentList = referenceName.TypeArgumentList
                if isNull typeArgumentList || typeArgumentList.TypeUsages.Count = 0 then () else

                let typesCount = typeArgumentList.TypeUsages.Count
                candidates <- candidates |> Seq.filter (fun c -> c.TypeParameters.Count = typesCount)

            let typeReferenceName = context.As<ITypeReferenceName>()
            if isNotNull (AttributeNavigator.GetByReferenceName(typeReferenceName)) then
                let attributeTypeElement = context.GetPredefinedType().Attribute.GetTypeElement()
                candidates <- candidates |> Seq.filter (fun c -> c.IsDescendantOf(attributeTypeElement))

            if isNotNull (InheritMemberNavigator.GetByTypeName(typeReferenceName)) then
                candidates <- candidates |> Seq.filter (fun c -> c :? IInterface || c :? IClass || c :? IStruct)

            candidates

        member x.ReferenceTargetCanBeType _ = true
        member x.ReferenceTargetIsUnlikelyBeType _ = false


[<Language(typeof<FSharpLanguage>)>]
type FSharpQuickFixUtilComponent() =
    let [<Literal>] FcsOpName = "FSharpQuickFixUtilComponent.BindTo"

    member x.BindTo(reference: FSharpSymbolReference, typeElement: ITypeElement) =
        let referenceOwner = reference.GetElement()
        use writeCookie = WriteLockCookie.Create(referenceOwner.IsPhysical())

        FSharpReferenceBindingUtil.SetRequiredQualifiers(reference, typeElement)
        if FSharpResolveUtil.resolvesToQualified typeElement reference FcsOpName then reference else

        addOpens reference typeElement

    interface IFSharpQuickFixUtilComponent with
        member x.BindTo(reference, typeElement, _, _) =
            x.BindTo(reference :?> _, typeElement) :> _

        member x.AddImportsForExtensionMethod(reference, _) = reference

        member this.BindTo(reference, typeElement) =
            this.BindTo(reference :?> _, typeElement)


// todo: ExtensionMethodImportUtilBase
