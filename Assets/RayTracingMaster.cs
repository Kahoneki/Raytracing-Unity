using System.Collections.Generic;
using UnityEngine;

public class RayTracingMaster : MonoBehaviour
{

    [Header("User Config")]
    public int SphereSeed;
    public int bounceLimit;
    public float hueMin;
    public float hueMax;
    public float saturationMin;
    public float saturationMax;
    public float brightnessMin;
    public float brightnessMax;
    public float smoothnessMin;
    public float smoothnessMax;
    public float specularAmplitude;
    public float metalChance;
    public float emissiveChance;


    [Header("Components")]
    public ComputeShader RayTracingShader;
    private RenderTexture _target;

    private Camera _camera;
    public Texture _SkyboxTexture;

    private uint _currentSample = 0;
    private Material _addMaterial;

    public Light _directionalLight;

    private RenderTexture _converged;

    struct Sphere {
        public Vector3 position;
        public float radius;
        public Vector3 albedo;
        public Vector3 specular;
        public float smoothness;
        public Vector3 emission;
    };

    private Vector2 SphereRadiusRange = new Vector2(5.0f, 30.0f);
    private uint SpheresMax = 800;
    private float SpherePlacementRadius = 300.0f;
    private ComputeBuffer _sphereBuffer;


    private void OnEnable() {
        _currentSample = 0;
        SetUpScene();
    }


    private void OnDisable() {
        if (_sphereBuffer != null)
            _sphereBuffer.Release();
    }


    private void SetUpScene() {

        Random.InitState(SphereSeed);

        List<Sphere> spheres = new List<Sphere>();

        //Add a number of random spheres
        for (int i=0; i<SpheresMax; i++) {
            Sphere sphere = new Sphere();

            //Randomise sphere radius
            sphere.radius = SphereRadiusRange.x + Random.value * (SphereRadiusRange.y - SphereRadiusRange.x);

            //Randomise sphere pos
            Vector2 randomPos = Random.insideUnitCircle * SpherePlacementRadius;
            sphere.position = new Vector3(randomPos.x, sphere.radius, randomPos.y);

            //Reject spheres that are intersecting others
            foreach (Sphere other in spheres) {
                float minDist = sphere.radius + other.radius;
                if (Vector3.SqrMagnitude(sphere.position - other.position) < minDist * minDist)
                    goto SkipSphere;
            }

            //Randomise sphere albedo, specular, smoothness, and emission
            Color colour = Random.ColorHSV();
            Color emissionColour = Random.ColorHSV(hueMin, hueMax, saturationMin, saturationMax, brightnessMin, brightnessMax);
            bool metal = Random.value <= metalChance;
            bool emissive = Random.value <= emissiveChance;
            sphere.albedo = metal ? Vector3.zero : new Vector3(colour.r, colour.g, colour.b);
            sphere.specular = metal ? new Vector3(colour.r, colour.g, colour.b) : Vector3.one * specularAmplitude;
            sphere.smoothness = Random.Range(smoothnessMin, smoothnessMax);
            sphere.emission = emissive ? new Vector3(emissionColour.r, emissionColour.g, emissionColour.b) : Vector3.zero;

            spheres.Add(sphere);

            SkipSphere:
                continue;
        }

        //Assign compute buffer data
        _sphereBuffer = new ComputeBuffer(spheres.Count, 56);
        _sphereBuffer.SetData(spheres);
    }


    private void Awake() {
        _camera = GetComponent<Camera>();
    }

    private void Update() {
        if (transform.hasChanged || _directionalLight.transform.hasChanged) {
            _currentSample = 0;
            transform.hasChanged = false;
            _directionalLight.transform.hasChanged = false;
        }
    }

    private void SetShaderParameters() {
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
        RayTracingShader.SetTexture(0, "_SkyboxTexture", _SkyboxTexture);
        RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));
        RayTracingShader.SetInt("_BounceLimit", bounceLimit);
        RayTracingShader.SetFloat("_Seed", Random.value);
        
        Vector3 localLightForward = _directionalLight.transform.forward;
        RayTracingShader.SetVector("_DirectionalLight", new Vector4(localLightForward.x, localLightForward.y, localLightForward.z, _directionalLight.intensity));

        RayTracingShader.SetBuffer(0, "_Spheres", _sphereBuffer);
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination) {
        SetShaderParameters();
        Render(destination);
    }

    private void Render(RenderTexture destination) {
        //Catches if target is null of if screen dimensions have changed reinitialises texture
        if (_target == null || _target.width != Screen.width || _target.height != Screen.height)
            InitRenderTexture();

        //Initialising the additive shader for multiframe antialiasing
        if (_addMaterial == null)
            _addMaterial = new Material(Shader.Find("Hidden/AddShader"));
        _addMaterial.SetFloat("_Sample", _currentSample);

        //Set the target and dispatch the compute shader
        RayTracingShader.SetTexture(0, "Result", _target);
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        Graphics.Blit(_target, _converged, _addMaterial);
        Graphics.Blit(_converged, destination);
        _currentSample++;
    }

    private void InitRenderTexture() {
        //Release render texture if we already have one
        if (_target != null)
            _target.Release();
        if (_converged != null)
            _converged.Release();
        
        //Get a render target for Raytracing
        _target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        _target.enableRandomWrite = true;
        _target.Create();

        _converged = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        _converged.enableRandomWrite = true;
        _converged.Create();
    }
}
