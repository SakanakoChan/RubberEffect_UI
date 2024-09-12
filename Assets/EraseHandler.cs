using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class EraseHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    //擦除完成调用事件
    public Action eraseFinishEvent;

    //笔刷半径
    //brush radius (meaning the brush size)
    [SerializeField] int brushRadius = 50;

    //擦除比例，擦除比例高于该值，是为擦除完成，自动擦除剩余部分
    //if the erase progression is higher than this value, it'll see this as finished and auto erase the left part
    [SerializeField] float finishPercent = 0.9f;

    //擦除点偏移量,距离上个擦除点>=该值时开始新的擦除点
    //if the distance of the current mouse position to the previous mouse position is less than this value,
    //will not erase, to make performance better
    [SerializeField] float drawOffset = 10f;

    //是否以擦除完成
    bool isEraseFinished;

    //要擦除的图片
    RawImage eraseImage;
    Texture2D eraseTexture;
    //图片长宽
    int textureWidth;
    int textureHeight;

    //图片大小
    //代表的是Color[] 数组的长度，图片是由像素组成的，Color[]数组存的是所有像素点的颜色，因此Color[]就可以表示已修改的像素点有多少（每修改一个像素点就让length++）
    //Image is made up of pixels, each pixel has its own color. Color[] array stores all the pixels' color, so color[] array can be used to
    //show the erased pixel length
    float textureLength;
    //擦除部分图片大小
    float eraseLength;

    Camera mainCamera;

    void Awake()
    {
        eraseImage = GetComponentInChildren<RawImage>();
        mainCamera = Camera.main;
    }

    void Start()
    {
        Init();
    }

    void Init()
    {
        isEraseFinished = false;
        eraseLength = 0;

        //原擦除图片
        //RawIamge.mainTexture is readonly, RawImage.texture is writable
        //Texture2D originalTexture = (Texture2D)eraseImage.mainTexture;
        Texture2D originalTexture = (Texture2D)eraseImage.texture;

        //被擦除的图片，展示擦除过程
        //recreating a new texture based on the original image is to make the new texture has alpha (transparency) pass
        //so that this new texture can be erased (making the alpha value of the certain pixel 0)
        eraseTexture = new Texture2D(originalTexture.width, originalTexture.height, TextureFormat.ARGB32, false);
        textureWidth = eraseTexture.width;
        textureHeight = eraseTexture.height;

        //to make GetPixels() working, texture has to be set as "Read/Write" ticked in Unity inspector
        eraseTexture.SetPixels(originalTexture.GetPixels());

        //apply means copy the modifications stroed in CPU of this texture into GPU
        eraseTexture.Apply();


        Debug.Log(textureWidth + " - " + textureHeight);

        eraseImage.texture = eraseTexture;

        textureLength = eraseTexture.GetPixels().Length;
    }

    #region Pointer Event

    public void OnPointerDown(PointerEventData eventData)
    {
        if (isEraseFinished)
            return;

        tempLastPoint = eventData.position;
        ErasePoint(eventData.position);
    }

    Vector2 tempEventPoint;
    Vector2 tempLastPoint;
    public void OnDrag(PointerEventData eventData)
    {
        if (isEraseFinished)
            return;

        tempEventPoint = eventData.position;

        //距离上个擦除点 >= 该值时开始新的擦除点
        //if current mouse position is too close to the previous mouse position, will not erase to make performance better
        if (Vector2.Distance(tempLastPoint, tempEventPoint) < drawOffset)
        {
            return;
        }

        //擦除点
        //erase part of the image according to the mouse position
        ErasePoint(tempEventPoint);
        //记录点
        //record the mouse position
        tempLastPoint = tempEventPoint;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (isEraseFinished)
            return;

        ErasePoint(eventData.position);
    }

    #endregion

    Vector3 tempWorldPoint;
    Vector3 tempLocalPoint;
    Vector2Int pixelPos;
    void ErasePoint(Vector2 screenPos)
    {
        //将鼠标点击位置转换到世界位置
        //convert the mouse position to world position
        tempWorldPoint = mainCamera.ScreenToWorldPoint(screenPos);

        //将鼠标点击位置从世界位置转换到本地位置（相对于parent transform）的位置，
        //unity inspector中的transform显示的位置就是local position
        //convert the mouse position to local position (position related to its parent transform),
        //the transform values showed in unity inspector are all the local ones
        tempLocalPoint = transform.InverseTransformPoint(tempWorldPoint);

        //借助multiplier来让鼠标点击位置精确对应擦除位置
        //没有multiplier的话，假如mask或者image的scale不为1，点击位置和实际擦除位置就会有偏差
        //this multiplier is to help make the mouse click position exactly corresponded to the actual erase position
        //if not using this multiplier, the mouse click position will deviate from the actual erase position
        float multiplier = 1 / eraseImage.transform.localScale.x;

        //相对图片像素点坐标
        //image's coordinate is always bigger than 0, 从左往右，从下到上
        pixelPos.x = Mathf.RoundToInt(tempLocalPoint.x * multiplier + textureWidth / 2);
        pixelPos.y = Mathf.RoundToInt(tempLocalPoint.y * multiplier + textureHeight / 2);

        //点击位置是否在图片范围内
        //if the mouse position is inside the image
        if (pixelPos.x < 0 || pixelPos.x >= textureWidth || pixelPos.y < 0 || pixelPos.y >= textureHeight)
            return;

        //遍历笔刷长宽范围内像素点
        //iterate all the pixels inside the brush radius
        for (int i = -brushRadius; i <= brushRadius; i++)
        {
            //超左/右边界
            //if the iterated position is beyond the left/right border of the image, ignore it
            if (pixelPos.x + i < 0 || pixelPos.x + i >= textureWidth)
                continue;

            for (int j = -brushRadius; j <= brushRadius; j++)
            {
                //超上/下边界
                //if the iterated pixel is beyond the top/bottom border of the image, ignore it
                if (pixelPos.y + j < 0 || pixelPos.y + j >= textureHeight)
                    continue;

                //是否在圆形范围内
                //勾股定理，i,j代表所处坐标，圆心在unity中设置为了(0,0)，i^2 + j^2也就代表到圆心的距离的平方，必须要小于半径的平方
                //这里要求在unity中必须把图片设置在中心位置，如此这个不等式才可以正常生效
                //if the iterated position is inside the brush radius
                //maths, Pythagorean Theorem
                //here the Circle Center has to be set as (0,0), meaning the image has to be put in the middle of the UI canvas
                //otherwise this erase function will not work properly
                if (Mathf.Pow(i, 2) + Mathf.Pow(j, 2) > Mathf.Pow(brushRadius, 2))
                    continue;

                //像素点色值
                //获取当前遍历到的像素点的颜色
                //get the color of the iterated pixel
                Color color = eraseTexture.GetPixel(pixelPos.x + i, pixelPos.y + j);

                //判断透明度,是否已擦除
                //if the pixel is already erased (alpha value is set to 0), ignore it
                if (Mathf.Approximately(color.a, 0))
                    continue;

                //修改像素点透明度
                //erase the pixel by setting its alpha vale to 0
                color.a = 0;
                eraseTexture.SetPixel(pixelPos.x + i, pixelPos.y + j, color);

                //擦除数量统计
                //the erased pixels count increaments
                eraseLength++;
            }
        }
        eraseTexture.Apply();

        //判断擦除进度
        //refresh the erase progression
        RefreshEraseProgression();
    }

    float tempPercent;
    void RefreshEraseProgression()
    {
        if (isEraseFinished)
            return;

        tempPercent = eraseLength / textureLength;

        tempPercent = (float)Math.Round(tempPercent, 2);

        Debug.Log($"Erase progression: {tempPercent}");

        if (tempPercent >= finishPercent)
        {
            isEraseFinished = true;

            eraseImage.enabled = false;

            //触发结束事件
            if (eraseFinishEvent != null)
                eraseFinishEvent.Invoke();
        }
    }

}
