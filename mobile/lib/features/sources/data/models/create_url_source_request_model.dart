import 'package:flutter/foundation.dart';

@immutable
class CreateUrlSourceRequestModel {
  const CreateUrlSourceRequestModel({required this.url});

  final String url;

  Map<String, dynamic> toJson() => <String, dynamic>{
        'url': url.trim(),
      };
}
