using UnityEngine;

public class CameraController : AutoActor, ISubActor, ISubExits, ISubNode, ISubSentiment
{
    private Color color
    {
        get => _camera.backgroundColor;
        set => _camera.backgroundColor = value;
    }

    [SerializeField]
    private Vector2 depthRange;

    [SerializeField]
    private Vector2 heightRange;

    [SerializeField]
    private Vector3 maximumRotation;

    [SerializeField]
    private Vector3 minimumRotation;

    [SerializeField]
    private float colorBlendSpeed = 1.0f;

    [SerializeField]
    private Camera _camera;

    private VideoCallUIController _ui;

    private float _volume;
    private Color _color = Color.black;

    private bool _enabled = true;

    private void Awake()
    {
        var rotation = _camera.transform.parent.rotation.eulerAngles;
        rotation.x += Random.Range(minimumRotation.x, maximumRotation.x);
        rotation.y += Random.Range(minimumRotation.y, maximumRotation.y);
        rotation.z += Random.Range(minimumRotation.z, maximumRotation.z);
        _camera.transform.parent.rotation = Quaternion.Euler(rotation);

        var position = _camera.transform.localPosition;
        position.x = Random.Range(depthRange.x, depthRange.y);
        position.y = Random.Range(heightRange.x, heightRange.y);
        _camera.transform.localPosition = position;
    }

    private void Start()
    {
        if (VideoCallUIManager.Instance == null)
            DisableCamera();
        if (_enabled)
            _ui = VideoCallUIManager.Instance.RegisterUI(ActorController, _camera);
        ActorController.Camera = _camera;
    }

    private void Update()
    {
        if (!_enabled) return;
        if (!ActorController.IsTalking && !_ui.IsMuted && _volume > 0.1f)
            _ui.Mute();
        var d = Time.deltaTime;
        UpdateColor(d);
        UpdateVolume(d);
    }

    private void UpdateVolume(float d)
    {
        if (ActorController.TotalVolume > _volume)
            _volume = ActorController.TotalVolume;
        _volume -= colorBlendSpeed * d;
        if (_volume < 0)
            _volume = 0;
        _ui.SetBorderOpacity(_volume == 0 ? 0 : 1);
    }

    private void UpdateColor(float d)
    {
        color = color.Lerp(_color, colorBlendSpeed * d);
    }

    public void AddColor(Color target, float blend = 0.5f)
    {
        target = target.Darken();
        _color = color.Lerp(target, blend);
    }

    public void UpdateSentiment(Sentiment sentiment)
    {
        if (!_enabled) return;
        if (sentiment == null) return;
        AddColor(sentiment.Color);
    }

    public void UpdateActor(ActorContext context)
    {
        if (!_enabled) return;
        _camera.backgroundColor = context.Reference.Color
            .Darken();
    }

    public void Activate(ChatNode node)
    {
        if (!_enabled) return;
        if (!_ui.IsVisible)
            _ui.Show();
        if (_ui.IsMuted)
            _ui.Unmute();
        AddColor(node.Actor.Color);
    }

    public void Deactivate()
    {
        if (!_enabled) return;
        VideoCallUIManager.Instance.RemoveUI(_ui);
    }

    public void DisableCamera()
    {
        _camera.gameObject.SetActive(false);
        _enabled = false;
    }
}