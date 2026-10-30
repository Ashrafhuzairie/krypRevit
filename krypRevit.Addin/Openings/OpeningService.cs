using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using krypRevit.Addin.Endpoints;
namespace krypRevit.Addin.Openings
{
    public sealed class OpeningRequest
    {
        public string[] service_ids {get;set;}
        public string[] host_ids {get;set;}
        public string link_id {get;set;}
        public string service_kind {get;set;}="all";
        public string shape {get;set;}="auto";
    }
    public sealed class OpeningSpec
    {
        public string host_unique_id {get;set;}
        public string service_unique_id {get;set;}
        public string link_unique_id {get;set;}
        public string host_id {get;set;}
        public string service_id {get;set;}
        public string service_kind {get;set;}
        public string shape {get;set;}
        public string method {get;set;}
        public double[] center_ft {get;set;}
        public double[] x_axis {get;set;}
        public double[] y_axis {get;set;}
        public double[] normal {get;set;}
        public double[] service_start_ft {get;set;}
        public double[] service_end_ft {get;set;}
        public double width_mm {get;set;}
        public double height_mm {get;set;}
        public double diameter_mm {get;set;}
        public double nominal_service_mm {get;set;}
        public string rule {get;set;}
    }
    public sealed class OpeningBatchResult
    {
        public string[] created;
        public string[] modified;
        public object[] results;
    }
    public static class OpeningService
    {
        private static XYZ P(double[] p)=>new XYZ(p[0],p[1],p[2]);
        private static double[] A(XYZ p)=>new[]{p.X,p.Y,p.Z};
        private static double Mm(double feet)=>UnitUtils.ConvertFromInternalUnits(feet,UnitTypeId.Millimeters);
        private static double Ft(double mm)=>UnitUtils.ConvertToInternalUnits(mm,UnitTypeId.Millimeters);
        private static string Kind(Element e,OpeningRules rules)
        {
            long id=e.Category?.Id.Value ?? 0;
            if(id==(long)BuiltInCategory.OST_CableTray)
            {
                var type=e.Document.GetElement(e.GetTypeId());
                var text=e.Name+" "+type?.Name+" "+(type as ElementType)?.FamilyName;
                return rules.ClassifyTray(text);
            }
            if(id==(long)BuiltInCategory.OST_PipeCurves)return "pipe";
            if(id==(long)BuiltInCategory.OST_DuctCurves)return "duct";
            if(id==(long)BuiltInCategory.OST_Conduit)return "conduit";
            return null;
        }
        private static IEnumerable<Solid> Solids(GeometryElement geometry)
        {
            if(geometry==null)yield break;
            foreach(var item in geometry)
            {
                if(item is Solid solid&&solid.Volume>1e-9)yield return solid;
                if(item is GeometryInstance instance)foreach(var child in Solids(instance.GetInstanceGeometry()))yield return child;
            }
        }
        private static List<Solid> HostSolids(Element e)=>Solids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).ToList();
        private static XYZ Crossing(IEnumerable<Solid> solids,Curve axis,bool requireThrough=false)
        {
            foreach(var solid in solids)
            using(var options=new SolidCurveIntersectionOptions())
            using(var intersection=solid.IntersectWithCurve(axis,options))
                if(intersection.SegmentCount>0)
                {
                    var segment=intersection.GetCurveSegment(0);
                    if(requireThrough&&new[]{segment.GetEndPoint(0),segment.GetEndPoint(1)}.Any(p=>p.DistanceTo(axis.GetEndPoint(0))<1e-6||p.DistanceTo(axis.GetEndPoint(1))<1e-6))throw new ArgumentException("Service terminates inside/on the host; a through-penetration requires the service to extend beyond both faces.");
                    return segment.Evaluate(.5,true);
                }
            return null;
        }
        private static double[] Bounds(BoundingBoxXYZ box,Transform transform)
        {
            if(box==null)return null;var points=new List<XYZ>();
            foreach(var x in new[]{box.Min.X,box.Max.X})foreach(var y in new[]{box.Min.Y,box.Max.Y})foreach(var z in new[]{box.Min.Z,box.Max.Z})points.Add(transform.OfPoint(box.Transform.OfPoint(new XYZ(x,y,z))));
            return new[]{points.Min(p=>p.X),points.Max(p=>p.X),points.Min(p=>p.Y),points.Max(p=>p.Y),points.Min(p=>p.Z),points.Max(p=>p.Z)};
        }
        private static bool BoxesOverlap(double[] a,double[] b)=>a!=null&&b!=null&&a[1]>=b[0]&&a[0]<=b[1]&&a[3]>=b[2]&&a[2]<=b[3]&&a[5]>=b[4]&&a[4]<=b[5];
        public static List<EditAction> Discover(UIApplication app,OpeningRequest request,OpeningRules rules,out string[] notes)
        {
            var doc=Model.Document(app);rules.Validate();
            if(request==null)throw new ArgumentException("Opening scope is required.");
            if(request.shape!="auto"&&request.shape!="rectangular"&&request.shape!="circular")throw new ArgumentException("Shape must be auto, rectangular or circular.");
            if(!new[]{"all","cable_tray","trunking","pipe","duct","conduit"}.Contains(request.service_kind))throw new ArgumentException("Invalid service kind.");
            var link=string.IsNullOrWhiteSpace(request.link_id)?null:Model.Element(doc,request.link_id) as RevitLinkInstance;
            if(!string.IsNullOrWhiteSpace(request.link_id)&&link==null)throw new ArgumentException("link_id must identify a loaded Revit link instance.");
            var source=link==null?doc:link.GetLinkDocument();if(source==null)throw new ArgumentException("The MEP link is not loaded.");
            var selected=app.ActiveUIDocument.Selection.GetElementIds().Select(id=>doc.GetElement(id)).Where(e=>e!=null).ToArray();
            IEnumerable<Element> services;
            if(request.service_ids!=null&&request.service_ids.Length>0)services=request.service_ids.Select(id=>Model.Element(source,id));
            else if(link!=null)services=new FilteredElementCollector(source).OfClass(typeof(MEPCurve)).Cast<Element>();
            else services=selected.Where(e=>e is MEPCurve);
            var candidates=services.GroupBy(e=>e.UniqueId).Select(g=>g.First()).Where(e=>Kind(e,rules)!=null).Where(e=>request.service_kind=="all"||Kind(e,rules)==request.service_kind).Take(101).ToArray();
            if(candidates.Length==0)throw new ArgumentException("Select straight MEP services in Revit, or provide service_ids and an optional loaded link_id.");
            if(candidates.Length>100)throw new ArgumentException("More than 100 services in scope. Narrow the service IDs or selection.");
            IEnumerable<Element> hosts;
            if(request.host_ids!=null&&request.host_ids.Length>0)hosts=request.host_ids.Select(id=>Model.Element(doc,id));
            else if(selected.Any(e=>e is Wall||e is Floor))hosts=selected.Where(e=>e is Wall||e is Floor);
            else hosts=new FilteredElementCollector(doc).WhereElementIsNotElementType().Cast<Element>().Where(e=>e is Wall||e is Floor);
            var hostArray=hosts.GroupBy(e=>e.UniqueId).Select(g=>g.First()).Take(1001).ToArray();
            if(hostArray.Length>1000)throw new ArgumentException("More than 1000 hosts in scope. Provide specific host_ids.");
            if(hostArray.Any(e=>!(e is Wall)&&!(e is Floor)))throw new ArgumentException("Opening hosts must be walls or slabs in the active model.");
            var errors=new List<string>();var actions=new List<EditAction>();
            var transform=link?.GetTotalTransform()??Transform.Identity;
            var hostBounds=hostArray.ToDictionary(h=>h.UniqueId,h=>Bounds(h.get_BoundingBox(null),Transform.Identity));
            foreach(var service in candidates)
            {
            var serviceBounds=Bounds(service.get_BoundingBox(null),transform);
            foreach(var host in hostArray)
            {
                if(!BoxesOverlap(hostBounds[host.UniqueId],serviceBounds))continue;
                var action=new EditAction {operation="create_service_opening",unique_id=host.UniqueId,service_unique_id=service.UniqueId,link_unique_id=link?.UniqueId,opening_shape=request.shape};
                try
                {
                    var spec=Build(doc,action,rules);
                    if(spec==null)continue;
                    actions.Add(action);
                    if(actions.Count>10)throw new InvalidOperationException("More than ten penetrations found. Narrow the selected services/host IDs before staging.");
                }
                catch(InvalidOperationException){throw;}
                catch(ArgumentException ex){errors.Add("Host "+host.Id.Value+", service "+service.Id.Value+": "+ex.Message);}
            }
            }
            notes=errors.Distinct().Take(30).ToArray();
            if(actions.Count==0)throw new ArgumentException("No supported uncut perpendicular penetrations found. "+string.Join("; ",notes));
            return actions;
        }
        public static OpeningSpec Build(Document doc,EditAction action,OpeningRules rules)
        {
            var host=Model.Element(doc,action.unique_id);
            if(host.Pinned||host.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("Pinned/grouped hosts are not supported.");
            var link=string.IsNullOrEmpty(action.link_unique_id)?null:Model.Element(doc,action.link_unique_id) as RevitLinkInstance;
            if(!string.IsNullOrEmpty(action.link_unique_id)&&link==null)throw new ArgumentException("Invalid service link.");
            var source=link==null?doc:link.GetLinkDocument();if(source==null)throw new ArgumentException("Service link is unloaded.");
            var service=Model.Element(source,action.service_unique_id) as MEPCurve;
            if(service==null)throw new ArgumentException("Service must be a cable tray, trunking cable tray, pipe, duct or conduit MEPCurve.");
            var kind=Kind(service,rules);if(kind==null)throw new ArgumentException("Unsupported service category.");
            var line=(service.Location as LocationCurve)?.Curve as Line;
            if(line==null)throw new ArgumentException("Only straight services are supported.");
            var transform=link?.GetTotalTransform()??Transform.Identity;var axis=line.CreateTransformed(transform) as Line;
            var hostSolids=HostSolids(host);var crossing=Crossing(hostSolids,axis,true);if(crossing==null)return null;
            XYZ u,v,n,center=crossing;
            string method;
            if(host is Wall wall)
            {
                var baseline=(wall.Location as LocationCurve)?.Curve as Line;
                if(wall.WallType.Kind!=WallKind.Basic||baseline==null||wall.CrossSection!=WallCrossSection.Vertical)throw new ArgumentException("Wall must be straight, vertical and basic.");
                u=baseline.Direction.Normalize();v=XYZ.BasisZ;n=u.CrossProduct(v).Normalize();method="wall_rectangle";
            }
            else if(host is Floor floor)
            {
                var face=HostObjectUtils.GetTopFaces(floor).Select(r=>floor.GetGeometryObjectFromReference(r)).OfType<PlanarFace>().FirstOrDefault(f=>f.FaceNormal.Z>.99999&&f.Project(crossing)!=null&&f.IsInside(f.Project(crossing).UVPoint));
                if(face==null)throw new ArgumentException("Slab must have a horizontal planar top face at the penetration.");
                if(Math.Abs(axis.Direction.Z)<.99999)throw new ArgumentException("Slab penetrations must be perpendicular to a horizontal slab.");
                u=XYZ.BasisX;v=XYZ.BasisY;n=XYZ.BasisZ;
                center=axis.GetEndPoint(0)+axis.Direction*((face.Origin.Z-axis.GetEndPoint(0).Z)/axis.Direction.Z);method="slab_profile";
            }
            else throw new ArgumentException("Host must be a wall or slab in the active model.");
            if(Math.Abs(axis.Direction.DotProduct(n))<.99999)throw new ArgumentException("Angled penetrations are not supported; the service must be perpendicular to the host.");
            var connector=service.ConnectorManager.Connectors.Cast<Connector>().FirstOrDefault(c=>c.ConnectorType==ConnectorType.End);
            if(connector==null)throw new ArgumentException("No service cross-section connector found.");
            bool round=connector.Shape==ConnectorProfileType.Round;
            if(!round&&connector.Shape!=ConnectorProfileType.Rectangular)throw new ArgumentException("Only round/rectangular service profiles are supported.");
            double diameter=round?2*connector.Radius:0;
            double physical=diameter;
            var outer=service.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);if(round&&outer!=null&&outer.HasValue)physical=Math.Max(physical,outer.AsDouble());
            double nominal=diameter;
            var nominalParameter=kind=="pipe"?service.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM):null;
            if(round&&nominalParameter!=null&&nominalParameter.HasValue)nominal=nominalParameter.AsDouble();
            double width,height;
            if(round){width=height=physical;}
            else
            {
                var sx=transform.OfVector(connector.CoordinateSystem.BasisX).Normalize();var sy=transform.OfVector(connector.CoordinateSystem.BasisY).Normalize();
                width=Math.Abs(sx.DotProduct(u))*connector.Width+Math.Abs(sy.DotProduct(u))*connector.Height;
                height=Math.Abs(sx.DotProduct(v))*connector.Width+Math.Abs(sy.DotProduct(v))*connector.Height;
            }
            string shape=action.opening_shape??"auto";if(shape=="auto")shape=round?"circular":"rectangular";
            if(shape!="rectangular"&&shape!="circular")throw new ArgumentException("Invalid opening shape.");
            double outWidth=0,outHeight=0,outDiameter=0;string rule;
            if(round)
            {
                outDiameter=rules.Circle(Mm(nominal),Mm(physical));outWidth=outHeight=outDiameter;
                rule="Circular table: nominal service "+Mm(nominal)+" mm, physical diameter "+Mm(physical)+" mm -> opening "+outDiameter+" mm (200 mm cap).";
            }
            else
            {
                if(shape=="circular")throw new ArgumentException("Circular openings for rectangular services need a separately defined enclosing-circle rule; use rectangular here.");
                var size=rules.Rectangle(kind,Mm(width),Mm(height));outWidth=size.width_mm;outHeight=size.height_mm;
                center+=u*Ft(size.offset_x_mm)+v*Ft(size.offset_y_mm);
                var configured=kind=="cable_tray"?rules.cable_tray:kind=="trunking"?rules.trunking:rules.rectangular_duct;
                rule=kind+" profile "+Mm(width)+" x "+Mm(height)+" mm; clearances L/R/T/B = "+configured.left_mm+"/"+configured.right_mm+"/"+configured.top_mm+"/"+configured.bottom_mm+" mm.";
            }
            // Check the complete profile perimeter in host material at the penetration's mid-plane.
            var checkCenter=center-n*((center-crossing).DotProduct(n));var borders=new List<Curve>();
            if(shape=="circular") {borders.Add(Arc.Create(checkCenter,Ft(outDiameter)/2,0,Math.PI,u,v));borders.Add(Arc.Create(checkCenter,Ft(outDiameter)/2,Math.PI,2*Math.PI,u,v));}
            else
            {
                var corners=new[]{checkCenter-u*Ft(outWidth)/2-v*Ft(outHeight)/2,checkCenter+u*Ft(outWidth)/2-v*Ft(outHeight)/2,checkCenter+u*Ft(outWidth)/2+v*Ft(outHeight)/2,checkCenter-u*Ft(outWidth)/2+v*Ft(outHeight)/2};
                for(int i=0;i<4;i++)borders.Add(Line.CreateBound(corners[i],corners[(i+1)%4]));
            }
            foreach(var border in borders)
            {
                double inside=0;
                foreach(var solid in hostSolids)using(var options=new SolidCurveIntersectionOptions())using(var intersection=solid.IntersectWithCurve(border,options))
                    for(int i=0;i<intersection.SegmentCount;i++)inside+=intersection.GetCurveSegment(i).Length;
                if(inside+1e-6<border.Length)throw new ArgumentException("The requested opening perimeter reaches a host edge or an existing void. Narrow/reposition the opening scope.");
            }
            if(host is Wall w&&shape=="circular") {if(!w.CanHaveProfileSketch())throw new ArgumentException("This wall cannot accept an editable-profile circular cut.");method="wall_profile_circle";}
            return new OpeningSpec {host_unique_id=host.UniqueId,service_unique_id=service.UniqueId,link_unique_id=link?.UniqueId,host_id=host.Id.Value.ToString(),service_id=service.Id.Value.ToString(),service_kind=kind,shape=shape,method=method,center_ft=A(center),x_axis=A(u),y_axis=A(v),normal=A(n),service_start_ft=A(axis.GetEndPoint(0)),service_end_ft=A(axis.GetEndPoint(1)),width_mm=outWidth,height_mm=outHeight,diameter_mm=shape=="circular"?outDiameter:0,nominal_service_mm=Mm(nominal),rule=rule};
        }
        public static bool Same(OpeningSpec a,OpeningSpec b)
        {
            if(a==null||b==null||a.host_unique_id!=b.host_unique_id||a.service_unique_id!=b.service_unique_id||a.link_unique_id!=b.link_unique_id||a.shape!=b.shape||a.method!=b.method||a.service_kind!=b.service_kind)return false;
            foreach(var pair in new[]{new[]{a.nominal_service_mm,b.nominal_service_mm},new[]{a.width_mm,b.width_mm},new[]{a.height_mm,b.height_mm},new[]{a.diameter_mm,b.diameter_mm}})if(Math.Abs(pair[0]-pair[1])>1e-6)return false;
            foreach(var pair in new[]{new[]{a.center_ft,b.center_ft},new[]{a.x_axis,b.x_axis},new[]{a.y_axis,b.y_axis},new[]{a.service_start_ft,b.service_start_ft},new[]{a.service_end_ft,b.service_end_ft}})if(pair[0].Zip(pair[1],(x,y)=>Math.Abs(x-y)).Any(d=>d>1e-7))return false;
            return true;
        }
        public static string Describe(OpeningSpec s)=>"CUT "+s.shape+" opening in host "+s.host_id+" for "+s.service_kind+" "+s.service_id+(s.link_unique_id==null?"":" (linked service)")+": "+(s.shape=="circular"?"diameter "+s.diameter_mm+" mm":s.width_mm+" x "+s.height_mm+" mm")+". "+s.rule+" Method: "+s.method+". X/Y are host-local left/right and top/bottom (slab: internal X/Y).";
        private sealed class Failures:IFailuresPreprocessor
        {
            public readonly List<string> Messages=new List<string>();
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a){foreach(var m in a.GetFailureMessages())Messages.Add(m.GetDescriptionText());return Messages.Count>0?FailureProcessingResult.ProceedWithRollBack:FailureProcessingResult.Continue;}
        }
        private static void Commit(Transaction tx,Failures f){if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Opening transaction rolled back: "+string.Join("; ",f.Messages));}
        private static Transaction Start(Document doc,string name,Failures f){var tx=new Transaction(doc,name);if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Could not start opening transaction.");tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(f).SetClearAfterRollback(true).SetForcedModalHandling(true));return tx;}
        public static OpeningBatchResult Apply(Document doc,OpeningSpec[] specs)
        {
            var created=new List<string>();var modified=new List<string>();var results=new List<object>();var failures=new Failures();
            using(var group=new TransactionGroup(doc,"krypRevit service openings"))
            {
                if(group.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Cannot start opening transaction group.");
                try
                {
                    foreach(var s in specs)
                    {
                        var host=Model.Element(doc,s.host_unique_id);var cutIds=new List<string>();var center=P(s.center_ft);var u=P(s.x_axis);var v=P(s.y_axis);
                        if(s.method=="wall_profile_circle")
                        {
                            var wall=(Wall)host;
                            using(var scope=new SketchEditScope(doc,"Circular wall opening"))
                            {
                                bool editing=false;
                                try
                                {
                                if(wall.SketchId==ElementId.InvalidElementId)scope.StartWithNewSketch(wall.Id);else scope.Start(wall.SketchId);editing=true;
                                using(var tx=Start(doc,"Create circular profile void",failures))
                                {
                                    doc.Regenerate();var sketch=(Sketch)doc.GetElement(wall.SketchId);var plane=sketch.SketchPlane.GetPlane();
                                    center-=plane.Normal*((center-plane.Origin).DotProduct(plane.Normal));
                                    var loops=sketch.Profile.Cast<CurveArray>().Select(loop=>{double value=0;var points=loop.Cast<Curve>().SelectMany(c=>c.Tessellate()).ToArray();for(int i=0;i<points.Length;i++){var a=points[i]-plane.Origin;var b=points[(i+1)%points.Length]-plane.Origin;value+=a.DotProduct(u)*b.DotProduct(v)-b.DotProduct(u)*a.DotProduct(v);}return new {Area=value,Points=points};}).OrderByDescending(loop=>Math.Abs(loop.Area)).ToArray();
                                    double area=loops[0].Area;double r=Ft(s.diameter_mm)/2;
                                    foreach(var loop in loops.Skip(1))
                                    {
                                        var xs=loop.Points.Select(point=>(point-center).DotProduct(u)).ToArray();var ys=loop.Points.Select(point=>(point-center).DotProduct(v)).ToArray();
                                        if(xs.Max()>=-r&&xs.Min()<=r&&ys.Max()>=-r&&ys.Min()<=r)throw new ArgumentException("Circular profile cut is too close to an existing profile void; merging/nested holes is not supported.");
                                    }
                                    var circleY=area>=0?-v:v;double radius=Ft(s.diameter_mm)/2;
                                    var first=doc.Create.NewModelCurve(Arc.Create(center,radius,0,Math.PI,u,circleY),sketch.SketchPlane);
                                    var second=doc.Create.NewModelCurve(Arc.Create(center,radius,Math.PI,2*Math.PI,u,circleY),sketch.SketchPlane);
                                    cutIds.Add(first.UniqueId);cutIds.Add(second.UniqueId);Commit(tx,failures);
                                }
                                scope.Commit(failures);editing=false;
                                }
                                catch {if(editing){try{scope.Cancel();}catch{}}throw;}
                            }
                        }
                        else using(var tx=Start(doc,"Create service opening",failures))
                        {
                            Opening opening;
                            if(host is Wall wall)opening=doc.Create.NewOpening(wall,center-u*Ft(s.width_mm)/2-v*Ft(s.height_mm)/2,center+u*Ft(s.width_mm)/2+v*Ft(s.height_mm)/2);
                            else
                            {
                                var curves=new CurveArray();
                                if(s.shape=="circular") {curves.Append(Arc.Create(center,Ft(s.diameter_mm)/2,0,Math.PI,u,v));curves.Append(Arc.Create(center,Ft(s.diameter_mm)/2,Math.PI,2*Math.PI,u,v));}
                                else {var a=center-u*Ft(s.width_mm)/2-v*Ft(s.height_mm)/2;var b=center+u*Ft(s.width_mm)/2-v*Ft(s.height_mm)/2;var c=center+u*Ft(s.width_mm)/2+v*Ft(s.height_mm)/2;var d=center-u*Ft(s.width_mm)/2+v*Ft(s.height_mm)/2;curves.Append(Line.CreateBound(a,b));curves.Append(Line.CreateBound(b,c));curves.Append(Line.CreateBound(c,d));curves.Append(Line.CreateBound(d,a));}
                                opening=doc.Create.NewOpening(host,curves,true);
                            }
                            cutIds.Add(opening.UniqueId);Commit(tx,failures);
                        }
                        // Confirm a real passage through the host, not just a marker or family symbol.
                        host=Model.Element(doc,s.host_unique_id);var remaining=HostSolids(host);
                        if(remaining.Count==0||Crossing(remaining,Line.CreateBound(P(s.service_start_ft),P(s.service_end_ft)))!=null)throw new InvalidOperationException("The service centerline is still blocked by host "+s.host_id+". Opening batch rolled back.");
                        created.AddRange(cutIds);modified.Add(host.UniqueId);results.Add(new {host_id=s.host_id,host_unique_id=s.host_unique_id,service_id=s.service_id,shape=s.shape,width_mm=s.width_mm,height_mm=s.height_mm,diameter_mm=s.diameter_mm,method=s.method,cut_unique_ids=cutIds.ToArray(),passage_verified=true});
                    }
                    if(group.Assimilate()!=TransactionStatus.Committed)throw new InvalidOperationException("Opening group did not commit.");
                }
                catch {if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
            }
            return new OpeningBatchResult {created=created.ToArray(),modified=modified.Distinct().ToArray(),results=results.ToArray()};
        }
    }
}
