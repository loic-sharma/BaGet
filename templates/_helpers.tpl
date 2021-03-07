{{/*
Set the common labels
*/}}
{{- define "baget.labels" -}}
app: {{ .Values.fullname }}
chart: {{ .Chart.Name }}
heritage: {{ .Release.Service | quote }}
release: {{ .Release.Name | quote }}
{{- end -}}

{{/*
Build image name
*/}}
{{- define "baget.image" -}}
{{- $image := default "loicsharma/baget" .Values.image -}}
{{- $imagetag := default "latest" .Values.imageVersion -}}
{{- printf "%s:%s" $image $imagetag -}}
{{- end -}}