using Cairo;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.API.Client
{
    /// <summary>
    /// Draws an icon
    /// </summary>
    public class IconComponent : RichTextComponentBase
    {
        protected ICoreClientAPI capi;

        public double sizeMulSvg = 0.7; // no idea why

        protected string iconName;
        public string iconPath;
        protected CairoFont font;
        protected bool UseSvgColor;
        protected double[] SvgBgColor;

        public IconComponent(ICoreClientAPI capi, string iconName, string iconPath, CairoFont font) : base(capi)
        {
            this.capi = capi;
            this.iconName = iconName;
            this.iconPath = iconPath;
            this.font = font;

            var size = GuiElement.scaled(font.UnscaledFontsize);
            BoundsPerLine = new LineRectangled[]
            {
                new LineRectangled(0, 0, size, size)
            };
        }

        public IconComponent(ICoreClientAPI capi, string iconName, string iconPath,CairoFont font, bool useSvgColor, double[] svgBgColor) : base(capi)
        {
            this.capi = capi;
            this.iconName = iconName;
            this.iconPath = iconPath;
            this.font = font;

            UseSvgColor = useSvgColor;
            SvgBgColor = svgBgColor;
            var size = GuiElement.scaled(font.UnscaledFontsize);
            BoundsPerLine = new LineRectangled[]
            {
                new LineRectangled(0, 0, size, size)
            };
        }


        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            IAsset svgAsset = null;
            if (iconPath != null)
            {
                svgAsset = capi.Assets.Get(new AssetLocation(iconPath).WithPathPrefixOnce("textures/"));
            }

            if (svgAsset != null) {
                var size = GuiElement.scaled(font.UnscaledFontsize);
                size = (int)(size * sizeMulSvg);
                var asc = font.GetFontExtents().Ascent;
                int? color = UseSvgColor ? null : ColorUtil.ColorFromRgba(font.Color);
                var posY = (int)(BoundsPerLine[0].Y + asc - size + 2); /* why the 2 offset? Only god knows -_- */

                if (VerticalAlign == EnumVerticalAlign.Middle)
                {
                    posY = (int)(BoundsPerLine[0].Y + GuiElement.scaled(1));
                }

                if (SvgBgColor != null)
                {
                    ctx.Operator = Operator.Over;
                    ctx.Rectangle((int)BoundsPerLine[0].X, posY, size, size);
                    ctx.SetSourceRGBA(SvgBgColor[0], SvgBgColor[1], SvgBgColor[2], SvgBgColor[3]);
                    ctx.Fill();
                }

                capi.Gui.DrawSvg(svgAsset, surface, (int)BoundsPerLine[0].X, posY, (int)size, (int)size, color);
            }
            else {
                double size = GuiElement.scaled(font.UnscaledFontsize);
                capi.Gui.Icons.DrawIcon(ctx, iconName, BoundsPerLine[0].X, BoundsPerLine[0].Y, size, size, font.Color);
            }
        }


        public override EnumCalcBoundsResult CalcBounds(TextFlowPath[] flowPath, double currentLineHeight, double offsetX, double lineY, out double nextOffsetX)
        {
            TextFlowPath curfp = GetCurrentFlowPathSection(flowPath, lineY);
            offsetX += GuiElement.scaled(PaddingLeft);
            bool requireLinebreak = offsetX + BoundsPerLine[0].Width > curfp.X2;

            this.BoundsPerLine[0].X = requireLinebreak ? 0 : offsetX;
            this.BoundsPerLine[0].Y = lineY + (requireLinebreak ? currentLineHeight : 0);

            nextOffsetX = (requireLinebreak ? 0 : offsetX) + BoundsPerLine[0].Width;

            return requireLinebreak ? EnumCalcBoundsResult.Nextline : EnumCalcBoundsResult.Continue;
        }

        public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
        {
            /*for (int i = 0; i < BoundsPerLine.Length; i++)
            {
                var bounds = BoundsPerLine[i];
                api.Render.RenderRectangle((float)renderX + (float)bounds.X, (float)renderY + (float)bounds.Y, 50, (float)bounds.Width, (float)bounds.Height, ColorUtil.ColorFromRgba(200, 255, 255, 128));
            }*/
        }


        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
