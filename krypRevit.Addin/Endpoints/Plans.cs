using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace krypRevit.Addin.Endpoints
{
    public static class Plans
    {
        private sealed class Plan
        {
            public string Id = Guid.NewGuid().ToString("N");
            public Document Document;
            public long Revision;
            public DateTime Expires = DateTime.UtcNow.AddMinutes(10);
            public List<EditAction> Actions;
            public string[] Summary;
            public string State = "pending_review";
            public string Error;
            public string[] Created = new string[0];
            public string[] Modified = new string[0];
            public object[] After = new object[0];
            public object[] Parameters = new object[0];
            public string ResultWarning;
        }
        private static Plan current;
        private static long revision;
        public static void DocumentChanged() { Interlocked.Increment(ref revision); }
        public static void Clear() { current = null; }
        private static double Number(object value) => ModellingRules.Number(value);
        private static XYZ Point(double[] value)
        { ModellingRules.Point(value);var p=new XYZ(value[0],value[1],value[2]);if(!XYZ.IsWithinLengthLimits(p)) throw new ArgumentException("Coordinates exceed Revit design limits.");return p; }
        private static Parameter Parameter(Element e, EditAction a)
        {
            long id;
            if (!long.TryParse(a.parameter_id, out id)) throw new ArgumentException("Use parameter_id returned by inspect_element.");
            var p = e.Parameters.Cast<Parameter>().SingleOrDefault(x => x.Id.Value == id);
            if (p == null || p.IsReadOnly) throw new ArgumentException("Parameter missing or read-only.");
            if (p.StorageType == StorageType.String)
            {
                if (!(a.value is string) || ((string)a.value).Length > 2000) throw new ArgumentException("Expected text, at most 2000 characters.");
            }
            else if (p.StorageType == StorageType.Double) Number(a.value);
            else if (p.StorageType == StorageType.Integer)
            {
                var n = Number(a.value);
                if (n != Math.Truncate(n) || n < int.MinValue || n > int.MaxValue) throw new ArgumentException("Expected an integer.");
                if (p.Definition.GetDataType() == SpecTypeId.Boolean.YesNo && n != 0 && n != 1) throw new ArgumentException("Yes/no values must be 0 or 1.");
            }
            else throw new ArgumentException("Only text, integer and double parameters can be edited.");
            return p;
        }
        private static string Describe(Document doc, EditAction a)
        {
            ModellingRules.Validate(a,doc.Application.ShortCurveTolerance);
            if (a.operation == "create_wall")
            {
                var type = Model.Element(doc, a.wall_type_unique_id) as WallType;
                var level = Model.Element(doc, a.level_unique_id) as Level;
                if (type == null || type.Kind != WallKind.Basic || level == null) throw new ArgumentException("Choose a basic wall type and level from inspect_model.");
                var start = Point(a.start_ft); var end = Point(a.end_ft);
                if (Math.Abs(start.Z - end.Z) > 1e-6 || Math.Abs(start.Z - level.ProjectElevation) > 1e-6)
                    throw new ArgumentException("Wall endpoints must lie on the selected level elevation.");
                if (start.DistanceTo(end) < doc.Application.ShortCurveTolerance) throw new ArgumentException("Wall is too short.");
                if (Number(a.height_ft) <= 0 || a.height_ft > 30000) throw new ArgumentException("Wall height must be positive and at most 30,000 feet.");
                return $"CREATE basic wall '{type.Name}', level '{level.Name}', centerline from {Json.Serialize(a.start_ft)} to {Json.Serialize(a.end_ft)} ft, height {a.height_ft} ft, nonstructural, unflipped, zero offset.";
            }
            if(a.operation=="create_floor")
            {
                var type=Model.Element(doc,a.floor_type_unique_id) as FloorType;
                var level=Model.Element(doc,a.level_unique_id) as Level;
                if(type==null || type.IsFoundationSlab || level==null) throw new ArgumentException("Choose a floor type (not a foundation slab) and existing level from inspection.");
                var boundary=FloorBoundary(doc,a,level);
                if(!BoundaryValidation.IsValidHorizontalBoundary(new List<CurveLoop>{boundary})) throw new ArgumentException("Revit rejected the floor boundary.");
                return $"CREATE nonstructural floor '{type.Name}', level '{level.Name}', zero offset, boundary {Json.Serialize(a.boundary_ft)} feet. Thickness follows the selected type.";
            }
            if(a.operation=="create_level")
            {
                if(new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Any(l=>string.Equals(l.Name,a.name,StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A level with this name already exists.");
                return $"CREATE level '{a.name}' at {a.elevation_ft.Value} feet (project internal coordinates). No plan views are created automatically.";
            }
            if(a.operation=="place_family")
            {
                var symbol=Model.Element(doc,a.symbol_unique_id) as FamilySymbol;
                var level=Model.Element(doc,a.level_unique_id) as Level;
                if(!Model.CanPlace(symbol) || level==null) throw new ArgumentException("Choose a loaded, unhosted one-level family symbol and existing level. Hosted/work-plane/adaptive/two-level families are unsupported.");
                if(symbol.Category!=null && symbol.Category.Id.Value==(long)BuiltInCategory.OST_StructuralColumns) throw new ArgumentException("Structural family placement is unsupported by this operation.");
                return $"PLACE family '{symbol.FamilyName}: {symbol.Name}', level '{level.Name}', at {Json.Serialize(a.point_ft)} feet, nonstructural.";
            }
            var e = Model.Element(doc, a.unique_id);
            if (e is ElementType || e is RevitLinkInstance || e.Pinned || e.GroupId != ElementId.InvalidElementId)
                throw new ArgumentException("Type elements, links, pinned and grouped elements cannot be edited by these tools.");
            var label = $"{e.Name} [id {e.Id.Value}, {e.UniqueId}]";
            switch (a.operation)
            {
                case "change_type":
                    var target=Model.Element(doc,a.type_unique_id) as ElementType;
                    if(target==null || !e.GetValidTypes().Contains(target.Id)) throw new ArgumentException("This type is not valid for the target instance; use valid_types from inspect_element.");
                    return $"CHANGE TYPE of {label} from '{doc.GetElement(e.GetTypeId())?.Name}' to '{target.Name}'. Revit may replace the instance.";
                case "set_wall_curve":
                    var wall=e as Wall;var location=wall?.Location as LocationCurve;
                    if(wall==null || wall.WallType.Kind!=WallKind.Basic || !(location?.Curve is Line)) throw new ArgumentException("Only straight basic walls can be reshaped.");
                    if(Math.Abs(Point(a.start_ft).Z-location.Curve.GetEndPoint(0).Z)>1e-6) throw new ArgumentException("Wall reshaping must preserve the current base elevation.");
                    return $"RESHAPE {label} from {Json.Serialize(Model.Coordinates(location.Curve.GetEndPoint(0)))} / {Json.Serialize(Model.Coordinates(location.Curve.GetEndPoint(1)))} to {Json.Serialize(a.start_ft)} / {Json.Serialize(a.end_ft)} feet. Joined geometry can change.";
                case "set_parameter":
                    var p = Parameter(e, a);
                    return $"SET {label}: {p.Definition.Name} ({a.parameter_id}, {p.StorageType}, spec {p.Definition.GetDataType().TypeId}) from {Json.Serialize(Model.Value(p))} to {Json.Serialize(a.value)}. Doubles use Revit internal units.";
                case "move": case "copy":
                    var v = Point(a.vector_ft);
                    if (v.GetLength() < 1e-9) throw new ArgumentException("Translation must be nonzero.");
                    return $"{a.operation.ToUpperInvariant()} {label} by {Json.Serialize(a.vector_ft)} feet. Copy may include dependent elements.";
                case "rotate":
                    Point(a.origin_ft);
                    if (Point(a.axis).GetLength() < 1e-9) throw new ArgumentException("Rotation axis must be nonzero.");
                    if (Math.Abs(Number(a.angle_radians)) > 2 * Math.PI || Math.Abs(a.angle_radians) < 1e-9) throw new ArgumentException("Rotation must be nonzero and within +/- 2 pi radians.");
                    return $"ROTATE {label} by {a.angle_radians} radians around {Json.Serialize(a.origin_ft)} ft, axis {Json.Serialize(a.axis)}.";
                default: throw new ArgumentException("Unsupported operation.");
            }
        }
        private static void Refresh()
        {
            if (current != null && current.State == "pending_review" &&
                (current.Expires <= DateTime.UtcNow || current.Revision != Interlocked.Read(ref revision)))
                current.State = "stale";
        }
        public static string Propose(UIApplication app, string json)
        {
            Refresh();
            if (current != null && current.State == "pending_review") throw new InvalidOperationException("A plan awaits review. Review or cancel it inside Revit first.");
            var doc = Model.Document(app);
            var request = Json.Deserialize<PlanRequest>(json);
            if (request?.actions == null || request.actions.Count == 0 || request.actions.Count > 10)
                throw new ArgumentException("Supply between 1 and 10 actions.");
            if (request.actions.Any(a => a == null)) throw new ArgumentException("Actions cannot be null.");
            // Disallow two operations on the same target; each preview must describe the actual pre-state.
            var ids = request.actions.Where(a => !ModellingRules.IsCreation(a.operation)).Select(a => a.unique_id).ToArray();
            if (ids.Distinct().Count() != ids.Length) throw new ArgumentException("Use only one action per element in a plan.");
            var summary = request.actions.Select(a => Describe(doc, a)).ToArray();
            current = new Plan { Document = doc, Revision = Interlocked.Read(ref revision), Actions = request.actions, Summary = summary };
            return Status();
        }
        public static string Status()
        {
            Refresh();
            if (current == null) return Json.Serialize(new { ok = true, state = "none" });
            return Json.Serialize(new { ok = true, plan_id=current.Id, state=current.State, document=current.Document.IsValidObject ? current.Document.Title : null,
                expires_utc=current.Expires.ToString("o"), summary=current.Summary, error=current.Error, created_unique_ids=current.Created, modified_unique_ids=current.Modified, elements_after=current.After, parameter_results=current.Parameters, result_warning=current.ResultWarning,
                next_step=current.State == "pending_review" ? "In Revit, click krypRevit > Review & apply. No model changes have been made." : null });
        }
        public static void Review(UIApplication app)
        {
            Refresh();
            var plan = current;
            if (plan == null || plan.State != "pending_review") { TaskDialog.Show("krypRevit", "No pending plan. Request a new proposal if the previous plan is stale."); return; }
            var doc = Model.Document(app);
            if (!ReferenceEquals(doc, plan.Document)) throw new InvalidOperationException("Switch back to the document used to propose this plan.");
            var dialog = new TaskDialog("krypRevit — review model changes") {
                MainInstruction = $"Review {plan.Actions.Count} action(s) in {doc.Title}",
                MainContent = string.Join("\n\n", plan.Summary.Select((s,i) => $"{i+1}. {s}")) + "\n\nRevit may adjust dependent or joined geometry. All changes are one undo step. Test on a copy first.",
                CommonButtons = TaskDialogCommonButtons.Cancel, DefaultButton = TaskDialogResult.Cancel
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Approve and apply these actions");
            if (dialog.Show() != TaskDialogResult.CommandLink1) { plan.State = "cancelled"; return; }
            Refresh();
            if (plan.State != "pending_review" || !ReferenceEquals(app.ActiveUIDocument?.Document, doc)) throw new InvalidOperationException("Plan expired or document changed. Propose again.");
            plan.State = "applying";
            try
            {
                foreach (var a in plan.Actions) Describe(doc, a);
                var created = new List<string>();
                var modified = new List<string>();
                var after = new List<object>();
                var failures=new RollbackFailures();
                using (var tx = new Transaction(doc, "krypRevit reviewed actions"))
                {
                    if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start transaction.");
                    var options = tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true).SetForcedModalHandling(true);
                    tx.SetFailureHandlingOptions(options);
                    foreach (var a in plan.Actions)
                    {
                        if (a.operation == "create_wall")
                        {
                            var wall = Wall.Create(doc, Line.CreateBound(Point(a.start_ft), Point(a.end_ft)), Model.Element(doc,a.wall_type_unique_id).Id,
                                Model.Element(doc,a.level_unique_id).Id, a.height_ft, 0, false, false);
                            created.Add(wall.UniqueId); continue;
                        }
                        if(a.operation=="create_floor")
                        {
                            var level=(Level)Model.Element(doc,a.level_unique_id);
                            var floor=Floor.Create(doc,new List<CurveLoop>{FloorBoundary(doc,a,level)},Model.Element(doc,a.floor_type_unique_id).Id,level.Id);
                            created.Add(floor.UniqueId);continue;
                        }
                        if(a.operation=="create_level")
                        {
                            var level=Level.Create(doc,a.elevation_ft.Value);level.Name=a.name;created.Add(level.UniqueId);continue;
                        }
                        if(a.operation=="place_family")
                        {
                            var symbol=(FamilySymbol)Model.Element(doc,a.symbol_unique_id);
                            if(!symbol.IsActive) {symbol.Activate();doc.Regenerate();}
                            var instance=doc.Create.NewFamilyInstance(Point(a.point_ft),symbol,(Level)Model.Element(doc,a.level_unique_id),Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                            created.Add(instance.UniqueId);continue;
                        }
                        var e = Model.Element(doc, a.unique_id);
                        switch (a.operation)
                        {
                            case "change_type":
                                var replacement=e.ChangeTypeId(Model.Element(doc,a.type_unique_id).Id);
                                modified.Add(replacement!=ElementId.InvalidElementId ? doc.GetElement(replacement).UniqueId : e.UniqueId);break;
                            case "set_wall_curve":((LocationCurve)e.Location).Curve=Line.CreateBound(Point(a.start_ft),Point(a.end_ft));modified.Add(e.UniqueId);break;
                            case "set_parameter":
                                var p = Parameter(e,a); bool changed;
                                if (p.StorageType == StorageType.String) changed = p.Set((string)a.value);
                                else if (p.StorageType == StorageType.Integer) changed = p.Set((int)Number(a.value));
                                else changed = p.Set(Number(a.value));
                                if (!changed && !Equals(Model.Value(p),a.value) && !(p.StorageType==StorageType.Double && Math.Abs(p.AsDouble()-Number(a.value))<1e-9)) throw new InvalidOperationException("Revit refused parameter change.");
                                modified.Add(e.UniqueId);
                                break;
                            case "move": ElementTransformUtils.MoveElement(doc,e.Id,Point(a.vector_ft)); modified.Add(e.UniqueId);break;
                            case "rotate": ElementTransformUtils.RotateElement(doc,e.Id,Line.CreateUnbound(Point(a.origin_ft),Point(a.axis).Normalize()),a.angle_radians); modified.Add(e.UniqueId);break;
                            case "copy":
                                foreach (var id in ElementTransformUtils.CopyElement(doc,e.Id,Point(a.vector_ft))) created.Add(doc.GetElement(id).UniqueId);
                                break;
                        }
                    }
                    doc.Regenerate();
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Batch rolled back: "+string.Join("; ",failures.Messages));
                }
                plan.Created = created.ToArray();plan.Modified=modified.Distinct().ToArray();plan.State = "applied";
                try
                {
                    foreach(var uid in created.Concat(modified).Distinct()) after.Add(Model.Describe(Model.Element(doc,uid)));
                    plan.After=after.ToArray();
                    plan.Parameters=plan.Actions.Where(a=>a.operation=="set_parameter").Select(a=>new {unique_id=a.unique_id,parameter_id=a.parameter_id,value_after=Model.Value(Parameter(Model.Element(doc,a.unique_id),a))}).Cast<object>().ToArray();
                }
                catch(Exception ex) {plan.After=after.ToArray();plan.ResultWarning="Changes committed, but some results could not be read back: "+ex.Message;}
                // A UI notification failure must never relabel a committed transaction as failed.
                try {TaskDialog.Show("krypRevit", "Actions committed. Use Revit Undo to undo the entire batch.");} catch { }
            }
            catch (Exception ex)
            {
                plan.State = "failed"; plan.Error = ex.Message;
                TaskDialog.Show("krypRevit", "Batch was not committed:\n" + ex.Message);
            }
        }
        public static string ShowIn3D(UIApplication app)
        {
            var doc=Model.Document(app);var plan=current;
            if(plan==null || plan.State!="applied" || !ReferenceEquals(plan.Document,doc)) throw new InvalidOperationException("Apply a proposal in the active document first.");
            var elements=plan.Created.Concat(plan.Modified).Distinct().Select(uid=>doc.GetElement(uid)).Where(e=>e!=null && e.Category!=null && e.Category.CategoryType==CategoryType.Model).ToArray();
            if(elements.Length==0) throw new InvalidOperationException("No surviving physical model results to show. Levels are datums; Undo may also have removed results.");
            var view=new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(v=>!v.IsTemplate && !v.IsPerspective).OrderByDescending(v=>v.Name=="{3D}").FirstOrDefault();
            if(view==null) throw new InvalidOperationException("Open an isometric 3D view in Revit first, then click Show in 3D.");
            app.ActiveUIDocument.ActiveView=view;
            var ids=elements.Select(e=>e.Id).ToArray();app.ActiveUIDocument.Selection.SetElementIds(ids);app.ActiveUIDocument.ShowElements(ids);
            return Json.Serialize(new {ok=true,count=ids.Length,view=view.Name});
        }
        private static CurveLoop FloorBoundary(Document doc,EditAction a,Level level)
        {
            var points=ModellingRules.Boundary(a.boundary_ft,doc.Application.ShortCurveTolerance);
            if(points.Any(p=>Math.Abs(p[2]-level.ProjectElevation)>1e-6)) throw new ArgumentException("Floor boundary Z must equal the selected level elevation; use parameter editing for an offset later.");
            var loop=new CurveLoop();
            for(int i=0;i<points.Length;i++) loop.Append(Line.CreateBound(Point(points[i]),Point(points[(i+1)%points.Length])));
            return loop;
        }
        private sealed class RollbackFailures : IFailuresPreprocessor
        {
            public readonly List<string> Messages=new List<string>();
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failures)
            {
                foreach(var failure in failures.GetFailureMessages()) Messages.Add(failure.GetDescriptionText());
                return Messages.Count>0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}
