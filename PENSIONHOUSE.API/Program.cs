using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
// Keep your own usings/namespaces for the repositories and JwtService here

var builder = WebApplication.CreateBuilder(args);

// Repositories
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton<EmailVerificationService>();
builder.Services.AddScoped<PaymentRejectionService>();
builder.Services.AddSingleton<IEmailSender, EmailSender>();
builder.Services.AddScoped<IRoomRepository, RoomRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IGuestRepository, GuestRepository>();
builder.Services.AddScoped<IAuthRepository, AuthRepository>();
builder.Services.AddScoped<IReviewRepository, ReviewRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IAuditRepository, AuditRepository>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<AccountStatus>();

// Controllers
builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// Swagger (with the Authorize button for JWT)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste only the token, without the word Bearer."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// JWT Service
builder.Services.AddSingleton<JwtService>();

// Authentication
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseCors("dev");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();

// Blocks a disabled guest or staff member right away (looked up on each request, remembered for 30 seconds)
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true
        && int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
    {
        var role = ctx.User.FindFirstValue(ClaimTypes.Role) ?? "";
        var status = ctx.RequestServices.GetRequiredService<AccountStatus>();
        if (!await status.IsActiveAsync(role, uid))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { message = "This account is disabled." });
            return;
        }
    }
    await next();
});

app.UseAuthorization();

// Marks signed-in admins and staff as "online" (the stored procedure writes at most once a minute per user)
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        var role = ctx.User.FindFirstValue(ClaimTypes.Role);
        if ((role == "admin" || role == "staff" || role == "user")
            && int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
        {
            try
            {
                await ctx.RequestServices.GetRequiredService<IAuditRepository>().TouchAsync(uid, role);
            }
            catch
            {
            }
        }
    }
    await next();
});

app.MapControllers();

app.Run();