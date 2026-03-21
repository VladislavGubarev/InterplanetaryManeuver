using InterplanetaryManeuver.App.Mvvm;
using PhysicsSim.Core;

namespace InterplanetaryManeuver.App.Models;

public sealed class EditableBody : ObservableObject
{
    private string _name = "Тело";
    private double _mass;
    private double _xAu;
    private double _yAu;
    private double _zAu;
    private double _vxKms;
    private double _vyKms;
    private double _vzKms;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double Mass
    {
        get => _mass;
        set => SetProperty(ref _mass, value);
    }

    public double XAu
    {
        get => _xAu;
        set => SetProperty(ref _xAu, value);
    }

    public double YAu
    {
        get => _yAu;
        set => SetProperty(ref _yAu, value);
    }

    public double ZAu
    {
        get => _zAu;
        set => SetProperty(ref _zAu, value);
    }

    public double VxKms
    {
        get => _vxKms;
        set => SetProperty(ref _vxKms, value);
    }

    public double VyKms
    {
        get => _vyKms;
        set => SetProperty(ref _vyKms, value);
    }

    public double VzKms
    {
        get => _vzKms;
        set => SetProperty(ref _vzKms, value);
    }

    public BodyState ToBodyState()
    {
        return new BodyState(
            Name,
            Mass,
            new Vector3d(
                XAu * AstronomyConstants.AstronomicalUnit,
                YAu * AstronomyConstants.AstronomicalUnit,
                ZAu * AstronomyConstants.AstronomicalUnit),
            new Vector3d(
                VxKms * 1000.0,
                VyKms * 1000.0,
                VzKms * 1000.0));
    }

    public EditableBody Clone()
    {
        return new EditableBody
        {
            Name = Name,
            Mass = Mass,
            XAu = XAu,
            YAu = YAu,
            ZAu = ZAu,
            VxKms = VxKms,
            VyKms = VyKms,
            VzKms = VzKms,
        };
    }
}

