namespace Codx.Auth.Services
{
    public class BuiltInEmailTemplateProvider
    {
        public string GetEmailVerificationBody(string displayName, string callbackUrl)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Email Verification</title>
    <style>
        body {{
            font-family: Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
        }}
        .header {{
            background-color: #007bff;
            color: white;
            padding: 20px;
            text-align: center;
            border-radius: 5px 5px 0 0;
        }}
        .content {{
            background-color: #f8f9fa;
            padding: 30px;
            border-radius: 0 0 5px 5px;
        }}
        .button {{
            display: inline-block;
            padding: 15px 30px;
            background-color: #007bff;
            color: white;
            text-decoration: none;
            border-radius: 5px;
            margin: 20px 0;
            font-weight: bold;
        }}
        .button:hover {{
            background-color: #0056b3;
        }}
        .warning {{
            background-color: #fff3cd;
            border: 1px solid #ffeaa7;
            border-radius: 5px;
            padding: 15px;
            margin: 20px 0;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            font-size: 12px;
            color: #6c757d;
        }}
        .link {{
            word-break: break-all;
            color: #007bff;
            font-size: 12px;
        }}
    </style>
</head>
<body>
    <div class=""header"">
        <h1>Welcome to Codx Auth!</h1>
    </div>
    <div class=""content"">
        <h2>Hello {displayName},</h2>
        <p>Thank you for registering with Codx Auth. To complete your registration, please verify your email address by clicking the button below:</p>
        
        <div style=""text-align: center;"">
            <a href=""{callbackUrl}"" class=""button"">Verify Email Address</a>
        </div>
        
        <div class=""warning"">
            <strong>Important:</strong>
            <ul>
                <li>This link will expire in 24 hours</li>
                <li>If you did not create this account, please ignore this email</li>
                <li>Do not share this link with anyone</li>
            </ul>
        </div>
        
        <p>If the button above doesn't work, copy and paste the following link into your browser:</p>
        <p class=""link"">{callbackUrl}</p>
        
        <p>Best regards,<br>Codx Auth Team</p>
    </div>
    <div class=""footer"">
        <p>This is an automated message. Please do not reply to this email.</p>
        <p>If you have any questions, please contact our support team.</p>
    </div>
</body>
</html>";
        }

        public string GetTwoFactorBody(string displayName, string code)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Two-Factor Authentication Code</title>
    <style>
        body {{
            font-family: Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
        }}
        .header {{
            background-color: #007bff;
            color: white;
            padding: 20px;
            text-align: center;
            border-radius: 5px 5px 0 0;
        }}
        .content {{
            background-color: #f8f9fa;
            padding: 30px;
            border-radius: 0 0 5px 5px;
        }}
        .verification-code {{
            background-color: #e9ecef;
            border: 2px solid #007bff;
            border-radius: 5px;
            font-size: 32px;
            font-weight: bold;
            text-align: center;
            padding: 20px;
            margin: 20px 0;
            letter-spacing: 5px;
            color: #007bff;
        }}
        .warning {{
            background-color: #fff3cd;
            border: 1px solid #ffeaa7;
            border-radius: 5px;
            padding: 15px;
            margin: 20px 0;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            font-size: 12px;
            color: #6c757d;
        }}
    </style>
</head>
<body>
    <div class=""header"">
        <h1>Two-Factor Authentication</h1>
    </div>
    <div class=""content"">
        <h2>Hello {displayName},</h2>
        <p>You are attempting to sign in to your account. To complete the login process, please use the verification code below:</p>
        
        <div class=""verification-code"">
            {code}
        </div>
        
        <div class=""warning"">
            <strong>Important:</strong>
            <ul>
                <li>This code will expire in 10 minutes</li>
                <li>Do not share this code with anyone</li>
                <li>If you did not request this code, please secure your account immediately</li>
            </ul>
        </div>
        
        <p>Enter this code on the verification page to complete your login.</p>
        
        <p>Best regards,<br>Codx Auth System</p>
    </div>
    <div class=""footer"">
        <p>This is an automated message. Please do not reply to this email.</p>
        <p>If you have any questions, please contact our support team.</p>
    </div>
</body>
</html>";
        }
    }
}
