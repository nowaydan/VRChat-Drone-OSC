using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using VRCDroneOSC.ViewModels;

namespace VRCDroneOSC.Views;

public partial class SimulatorView : UserControl
{
    private SimulatorViewModel? _vm;

    public SimulatorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is not MainViewModel main) return;

        _vm = new SimulatorViewModel(main);
        DataContext = _vm;
        _vm.StateUpdated += UpdateScene;

        BuildGridFloor();
        BuildDrone();
    }

    // ── Grid floor ───────────────────────────────────────────────────────────

    private void BuildGridFloor()
    {
        var group  = new Model3DGroup();
        var lineMat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0x80, 0x2D, 0x3A, 0x52)));
        var fillMat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x10, 0x20, 0x38)));

        const float size  = 20f;
        const int   cells = 20;
        float step = size / cells;

        // Flat base plane
        var baseGeo = new MeshGeometry3D();
        baseGeo.Positions.Add(new Point3D(-size / 2, 0, -size / 2));
        baseGeo.Positions.Add(new Point3D( size / 2, 0, -size / 2));
        baseGeo.Positions.Add(new Point3D( size / 2, 0,  size / 2));
        baseGeo.Positions.Add(new Point3D(-size / 2, 0,  size / 2));
        baseGeo.TriangleIndices.Add(0); baseGeo.TriangleIndices.Add(1); baseGeo.TriangleIndices.Add(2);
        baseGeo.TriangleIndices.Add(0); baseGeo.TriangleIndices.Add(2); baseGeo.TriangleIndices.Add(3);
        group.Children.Add(new GeometryModel3D(baseGeo, fillMat));

        // Grid lines — each line is a thin quad
        for (int i = 0; i <= cells; i++)
        {
            float pos = -size / 2 + i * step;
            const float thick = 0.04f;

            // Line along Z
            group.Children.Add(MakeLine(
                new Point3D(pos - thick, 0.001, -size / 2),
                new Point3D(pos + thick, 0.001, -size / 2),
                new Point3D(pos + thick, 0.001,  size / 2),
                new Point3D(pos - thick, 0.001,  size / 2),
                lineMat));

            // Line along X
            group.Children.Add(MakeLine(
                new Point3D(-size / 2, 0.001, pos - thick),
                new Point3D( size / 2, 0.001, pos - thick),
                new Point3D( size / 2, 0.001, pos + thick),
                new Point3D(-size / 2, 0.001, pos + thick),
                lineMat));
        }

        GridFloor.Content = group;
    }

    private static GeometryModel3D MakeLine(
        Point3D p0, Point3D p1, Point3D p2, Point3D p3,
        Material mat)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(p0); mesh.Positions.Add(p1);
        mesh.Positions.Add(p2); mesh.Positions.Add(p3);
        mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(2); mesh.TriangleIndices.Add(3);
        return new GeometryModel3D(mesh, mat);
    }

    // ── Drone model (cross shape: centre box + 4 arm boxes) ─────────────────

    private void BuildDrone()
    {
        var group    = new Model3DGroup();
        var bodyMat  = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)));
        var armMat   = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0x90)));

        // Centre body
        group.Children.Add(MakeBox(-0.15, 0, -0.15, 0.15, 0.08, 0.15, bodyMat));

        // 4 arms
        group.Children.Add(MakeBox( 0.15, -0.02, -0.04,  0.50, 0.04, 0.04, armMat));
        group.Children.Add(MakeBox(-0.50, -0.02, -0.04, -0.15, 0.04, 0.04, armMat));
        group.Children.Add(MakeBox(-0.04, -0.02,  0.15,  0.04, 0.04, 0.50, armMat));
        group.Children.Add(MakeBox(-0.04, -0.02, -0.50,  0.04, 0.04,-0.15, armMat));

        DroneVisual.Content = group;
    }

    private static GeometryModel3D MakeBox(
        double x0, double y0, double z0,
        double x1, double y1, double z1,
        Material mat)
    {
        var m = new MeshGeometry3D();

        // 8 corners
        var p = new[]
        {
            new Point3D(x0,y0,z0), new Point3D(x1,y0,z0),
            new Point3D(x1,y1,z0), new Point3D(x0,y1,z0),
            new Point3D(x0,y0,z1), new Point3D(x1,y0,z1),
            new Point3D(x1,y1,z1), new Point3D(x0,y1,z1)
        };
        foreach (var pt in p) m.Positions.Add(pt);

        // 6 faces × 2 triangles
        int[] faces = {
            0,2,1, 0,3,2,  // back  (z0)
            4,5,6, 4,6,7,  // front (z1)
            0,1,5, 0,5,4,  // bottom
            3,6,2, 3,7,6,  // top
            0,4,7, 0,7,3,  // left
            1,2,6, 1,6,5   // right
        };
        foreach (var i in faces) m.TriangleIndices.Add(i);

        return new GeometryModel3D(m, mat);
    }

    // ── Scene update (30 fps) ────────────────────────────────────────────────

    private void UpdateScene()
    {
        if (_vm == null) return;

        double x = _vm.PosX;
        double y = Math.Max(_vm.PosY, 0.0); // clamp above floor
        double z = _vm.PosZ;

        // Translate
        DroneTranslate.OffsetX = x;
        DroneTranslate.OffsetY = y + 0.5; // slight lift so it starts above floor
        DroneTranslate.OffsetZ = z;

        // Rotate using Euler angles (degrees) → quaternion
        double pitchRad = _vm.RotPitch * Math.PI / 180.0;
        double rollRad  = _vm.RotRoll  * Math.PI / 180.0;
        double yawRad   = _vm.RotYaw   * Math.PI / 180.0;

        var q = EulerToQuaternion(pitchRad, yawRad, rollRad);
        DroneQuat.Quaternion = new Quaternion(q.X, q.Y, q.Z, q.W);

        // Camera follows drone, offset behind and above
        double camX  = x;
        double camY  = y + 5.0;
        double camZ  = z - 10.0;
        Cam.Position = new Point3D(camX, camY, camZ);

        // Look toward drone centre
        var toTarget = new Vector3D(x - camX, (y + 0.5) - camY, z - camZ);
        toTarget.Normalize();
        Cam.LookDirection = toTarget;

        // Status text
        TxtPos.Text = $"X:{x:F1}  Y:{y:F1}  Z:{z:F1}";
    }

    private static (double X, double Y, double Z, double W) EulerToQuaternion(
        double pitch, double yaw, double roll)
    {
        // pitch = X, yaw = Y, roll = Z  (intrinsic XYZ)
        double cy = Math.Cos(yaw   * 0.5);
        double sy = Math.Sin(yaw   * 0.5);
        double cp = Math.Cos(pitch * 0.5);
        double sp = Math.Sin(pitch * 0.5);
        double cr = Math.Cos(roll  * 0.5);
        double sr = Math.Sin(roll  * 0.5);

        double w = cy * cp * cr + sy * sp * sr;
        double x = cy * sp * cr + sy * cp * sr;
        double y = sy * cp * cr - cy * sp * sr;
        double z = cy * cp * sr - sy * sp * cr;

        return (x, y, z, w);
    }
}
