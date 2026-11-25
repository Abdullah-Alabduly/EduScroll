class RegisterRequestModel {
  const RegisterRequestModel({
    required this.fullName,
    required this.userName,
    required this.email,
    required this.password,
  });

  final String fullName;
  final String userName;
  final String email;
  final String password;

  Map<String, dynamic> toJson() => {
        'fullName': fullName.trim(),
        'userName': userName.trim(),
        'email': email.trim(),
        'password': password,
      };

  @override
  String toString() =>
      'RegisterRequestModel(userName: $userName, password: [REDACTED])';
}
